shader_type canvas_item;

uniform float spectrum[64]; // 从C#传递的频谱数据
uniform float time_shift : hint_range(0, 1) = 0.1;
uniform float color_shift_speed : hint_range(0, 3) = 2.0;
uniform float sensitivity : hint_range(0, 1) = 0.3;
uniform float min_height : hint_range(0, 0.5) = 0.04;
uniform float randomness : hint_range(0, 1) = 0.3; // 随机性控制
uniform float rotation_speed : hint_range(-5, 5) = 1.0; // 新增：旋转速度控制

// 改进的伪随机函数，提供更平滑的结果
float rand(vec2 co) {
    return fract(sin(dot(co.xy, vec2(12.9898, 78.233))) * 43758.5453);
}

// 平滑插值函数
float smooth_interpolate(float a, float b, float t) {
    t = t * t * (3.0 - 2.0 * t);
    return mix(a, b, t);
}

// 改进的平滑HSV到RGB转换函数
vec3 hsv2rgb_smooth(float h, float s, float v) {
    vec3 c = vec3(h, s, v);
    vec3 rgb = clamp(abs(fract(c.x + vec3(0.0, 2.0, 1.0)/3.0) * 6.0 - 3.0) - 1.0, 0.0, 1.0);
    rgb = rgb * rgb * (3.0 - 2.0 * rgb);
    return c.z * mix(vec3(1.0), rgb, c.y);
}

// 平滑的噪声函数，减少突兀变化
float smooth_noise(vec2 co) {
    // 对四个角落进行采样
    vec2 i = floor(co);
    vec2 f = fract(co);

    // 四个角落的随机值
    float a = rand(i);
    float b = rand(i + vec2(1.0, 0.0));
    float c = rand(i + vec2(0.0, 1.0));
    float d = rand(i + vec2(1.0, 1.0));

    // 平滑插值
    vec2 u = f * f * (3.0 - 2.0 * f);
    return mix(a, b, u.x) + (c - a) * u.y * (1.0 - u.x) + (d - b) * u.x * u.y;
}

void fragment() {
    // 计算中心点坐标
    vec2 center = vec2(0.5, 0.5);

    // 应用旋转 - 新增部分
    vec2 coord = UV - center;
    float rotation_angle = TIME * rotation_speed;
    float cos_rot = cos(rotation_angle);
    float sin_rot = sin(rotation_angle);
    coord = vec2(
        coord.x * cos_rot - coord.y * sin_rot,
        coord.x * sin_rot + coord.y * cos_rot
    );

    // 计算当前像素相对于中心的角度和距离
    float angle = atan(coord.y, coord.x);
    float radius = length(coord);

    // 将角度映射到[0, 1]范围
    float normalized_angle = (angle + 3.14159265359) / 6.28318530718;

    // 使用环形分布 - 将角度映射到频谱数组索引
    // 添加更平滑的随机偏移以避免明显的接缝
    float random_offset = smooth_noise(vec2(TIME * 0.1, normalized_angle * 5.0)) * randomness * 0.5;
    float spectrum_pos = normalized_angle * 64.0 + random_offset;

    // 确保在有效范围内
    spectrum_pos = mod(spectrum_pos, 64.0);

    // 获取四个相邻点的频谱值用于平滑插值
    int index0 = int(floor(spectrum_pos));
    int index1 = (index0 + 1) % 64;
    int index2 = (index0 + 2) % 64;
    int index3 = (index0 + 3) % 64;

    float value0 = spectrum[index0] * sensitivity;
    float value1 = spectrum[index1] * sensitivity;
    float value2 = spectrum[index2] * sensitivity;
    float value3 = spectrum[index3] * sensitivity;

    // 计算插值因子
    float t = fract(spectrum_pos);

    // 使用Catmull-Rom样条插值获得平滑的频谱值
    float spectrum_value = value1 + 0.5 * t * (value2 - value0 +
        t * (2.0 * value0 - 5.0 * value1 + 4.0 * value2 - value3 +
        t * (3.0 * (value1 - value2) + value3 - value0)));

    // 确保最小值
    spectrum_value = max(spectrum_value, min_height);

    // 计算圆环的内外半径
    float inner_radius = 0.12;
    float outer_radius = inner_radius + spectrum_value;

    // 计算到圆环边缘的距离 (用于抗锯齿)
    float dist_to_inner = abs(radius - inner_radius);
    float dist_to_outer = abs(radius - outer_radius);
    float edge = fwidth(radius) * 2.0;

    // 检查当前像素是否在圆环范围内 (带抗锯齿)
    if (radius >= inner_radius && radius <= outer_radius) {
        // 基于时间和位置的颜色变化
        // 使用环形颜色分布，确保接缝处颜色连续
        float hue = fract(TIME * color_shift_speed * 0.05 + normalized_angle);

        // 使用平滑噪声替代随机值，减少颜色噪点
        float color_variation = smooth_noise(vec2(normalized_angle * 10.0, TIME * 0.1)) * 0.05;
        hue = fract(hue + color_variation);

        // 使用改进的HSV到RGB转换，提供更平滑的颜色过渡
        vec3 rgb = hsv2rgb_smooth(hue, 0.8, 1.0);

        // 基于频谱值增强亮度，同时保持平滑过渡
        float brightness = 0.7 + smoothstep(0.0, 2.0, spectrum_value) * 1.0;

        // 边缘抗锯齿
        float alpha = 1.0;
        if (dist_to_inner < edge) {
            alpha = smoothstep(0.0, edge, dist_to_inner);
        } else if (dist_to_outer < edge) {
            alpha = smoothstep(0.0, edge, dist_to_outer);
        }

        // 轻微的颜色模糊效果，进一步减少噪点
        float blur_factor = fwidth(radius) * 3.0;
        alpha *= smoothstep(0.0, blur_factor, spectrum_value);

        COLOR = vec4(rgb * brightness, alpha);
    } else {
        // 圆环外的像素透明
        COLOR = vec4(0.0);
    }
}