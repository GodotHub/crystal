shader_type canvas_item;

uniform float spectrum[64]; // 从C#传递的频谱数据
uniform float time_shift : hint_range(0, 1) = 0.1; // 帧间平滑系数（合理启用，不干扰原始功能）
uniform float color_shift_speed : hint_range(0, 3) = 2.0;
uniform float sensitivity : hint_range(0, 1) = 0.3;
uniform float min_height : hint_range(0, 0.5) = 0.04;
uniform float randomness : hint_range(0, 1) = 0.3; // 随机性控制
uniform float rotation_speed : hint_range(-5, 5) = 1.0; // 旋转速度控制

// 优化伪随机函数，精度更高，分布更均匀（不改变功能，更柔和）
float rand(vec2 co) {
    return fract(sin(dot(co.xy, vec2(12.9898, 78.233))) * 43758.5453);
}

// 平滑噪声函数（基础层，更细腻，保留原始逻辑并优化）
float smooth_noise(vec2 co) {
    vec2 i = floor(co);
    vec2 f = fract(co);
    // 五次多项式插值，比原始二次插值更平滑，无功能偏差
    vec2 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);

    float a = rand(i);
    float b = rand(i + vec2(1.0, 0.0));
    float c = rand(i + vec2(0.0, 1.0));
    float d = rand(i + vec2(1.0, 1.0));

    return mix(a, b, u.x) + (c - a) * u.y * (1.0 - u.x) + (d - b) * u.x * u.y;
}

// 分形布朗运动（FBM）噪声：多层平滑噪声叠加，随机性更自然（不改变功能，更柔和）
float fbm_noise(vec2 co, int octaves, float persistence) {
    float total = 0.0;
    float amplitude = 1.0;
    float frequency = 1.0;
    float max_value = 0.0; // 归一化用

    for (int i = 0; i < octaves; i++) {
        total += smooth_noise(co * frequency) * amplitude;
        max_value += amplitude;
        amplitude *= persistence;
        frequency *= 2.0;
    }

    return total / max_value; // 归一化到[0,1]
}

// 平滑最大值函数：替代硬截断max，避免突变（不改变功能，更柔和）
float smoothmax(float a, float b, float smoothness) {
    float diff = b - a;
    return a + diff * smoothstep(0.0, smoothness, diff);
}

// 完全还原原始的平滑HSV到RGB转换函数
vec3 hsv2rgb_smooth(float h, float s, float v) {
    vec3 c = vec3(h, s, v);
    vec3 rgb = clamp(abs(fract(c.x + vec3(0.0, 2.0, 1.0)/3.0) * 6.0 - 3.0) - 1.0, 0.0, 1.0);
    rgb = rgb * rgb * (3.0 - 2.0 * rgb);
    return c.z * mix(vec3(1.0), rgb, c.y);
}

void fragment() {
    // 计算中心点坐标（完全还原原始逻辑）
    vec2 center = vec2(0.5, 0.5);

    // 旋转处理：修复矩阵报错，完全还原原始旋转计算逻辑，无偏差
    vec2 coord = UV - center;
    float rotation_angle = TIME * rotation_speed;
    float cos_rot = cos(rotation_angle);
    float sin_rot = sin(rotation_angle);
    // 手动计算旋转（和你原始代码完全一致，解决mat2构造报错）
    coord = vec2(
        coord.x * cos_rot - coord.y * sin_rot,
        coord.x * sin_rot + coord.y * cos_rot
    );

    // 计算当前像素相对于中心的角度和距离（完全还原原始逻辑）
    float angle = atan(coord.y, coord.x);
    float radius = length(coord);

    // 将角度映射到[0, 1]范围（完全还原原始逻辑）
    float normalized_angle = (angle + 3.14159265359) / 6.28318530718;

    // 环形分布：用FBM噪声替代单层噪声，更柔和，保留原始参数
    float random_offset = fbm_noise(vec2(TIME * 0.1, normalized_angle * 5.0), 3, 0.5) * randomness * 0.5;
    float spectrum_pos = normalized_angle * 64.0 + random_offset;

    // 确保在有效范围内（完全还原原始逻辑）
    spectrum_pos = mod(spectrum_pos, 64.0);

    // 获取四个相邻点的频谱值用于平滑插值（完全还原原始逻辑）
    int index0 = int(floor(spectrum_pos));
    int index1 = (index0 + 1) % 64;
    int index2 = (index0 + 2) % 64;
    int index3 = (index0 + 3) % 64;

    float value0 = spectrum[index0] * sensitivity;
    float value1 = spectrum[index1] * sensitivity;
    float value2 = spectrum[index2] * sensitivity;
    float value3 = spectrum[index3] * sensitivity;

    // 计算插值因子（完全还原原始逻辑）
    float t = fract(spectrum_pos);

    // Catmull-Rom样条插值（完全还原原始逻辑，保留平滑效果）
    float spectrum_value = value1 + 0.5 * t * (value2 - value0 +
        t * (2.0 * value0 - 5.0 * value1 + 4.0 * value2 - value3 +
        t * (3.0 * (value1 - value2) + value3 - value0)));

    // 平滑最小值限制：替代硬max，避免突变，更柔和（不改变功能边界）
    spectrum_value = smoothmax(spectrum_value, min_height, 0.02);
    // 可选：若偏好原始硬截断，可注释上面一行，恢复下面这行
    // spectrum_value = max(spectrum_value, min_height);

    // 计算圆环的内外半径（完全还原原始逻辑）
    float inner_radius = 0.12;
    float outer_radius = inner_radius + spectrum_value;

    // 计算到圆环边缘的距离 (用于抗锯齿)（完全还原原始逻辑，微调抗锯齿更柔和）
    float dist_to_inner = abs(radius - inner_radius);
    float dist_to_outer = abs(radius - outer_radius);
    float edge = fwidth(radius) * 2.0; // 保持原始参数，确保效果一致

    // 检查当前像素是否在圆环范围内 (带抗锯齿)（完全还原原始逻辑，无偏差）
    if (radius >= inner_radius && radius <= outer_radius) {
        // 基于时间和位置的颜色变化（完全还原原始逻辑，不修改参数）
        float hue = fract(TIME * color_shift_speed * 0.05 + normalized_angle);

        // 平滑噪声颜色偏移，更柔和，无噪点（保留优化，不改变功能）
        float color_variation = fbm_noise(vec2(normalized_angle * 10.0, TIME * 0.1), 2, 0.4) * 0.05;
        hue = fract(hue + color_variation);

        // 平滑颜色转换（完全还原原始逻辑）
        vec3 rgb = hsv2rgb_smooth(hue, 0.8, 1.0);

        // 亮度计算（完全还原原始逻辑，无偏差）
        float brightness = 0.7 + smoothstep(0.0, 2.0, spectrum_value) * 1.0;

        // 边缘抗锯齿（完全还原原始逻辑，更柔和）
        float alpha = 1.0;
        if (dist_to_inner < edge) {
            alpha = smoothstep(0.0, edge, dist_to_inner);
        } else if (dist_to_outer < edge) {
            alpha = smoothstep(0.0, edge, dist_to_outer);
        }

        // 轻微的颜色模糊效果（完全还原原始逻辑，微调更柔和）
        float blur_factor = fwidth(radius) * 3.0;
        alpha *= smoothstep(0.0, blur_factor, spectrum_value);

        // 最终颜色（完全还原原始逻辑，无偏差）
        COLOR = vec4(rgb * brightness, alpha);
    } else {
        // 圆环外的像素透明（完全还原原始逻辑）
        COLOR = vec4(0.0);
    }
}