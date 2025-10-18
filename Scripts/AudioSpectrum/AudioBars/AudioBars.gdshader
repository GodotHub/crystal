shader_type canvas_item;

// 从C#传递的频谱数据
uniform float spectrum[128];
// 颜色渐变控制
uniform vec4 color_low : source_color = vec4(0.0, 0.0, 1.0, 1.0); // 低频颜色 (蓝色)
uniform vec4 color_mid : source_color = vec4(0.0, 0.0, 1.0, 1.0); // 中频颜色 (绿色)
uniform vec4 color_high : source_color = vec4(0.0, 0.0, 1.0, 1.0); // 高频颜色 (红色)
// 灵敏度控制
uniform float sensitivity = 1.0;
// 背景颜色
uniform vec4 bg_color : source_color = vec4(0.0, 0.0, 0.0, 0.5);

void fragment() {
    // 获取屏幕分辨率
    vec2 iResolution = 1.0 / SCREEN_PIXEL_SIZE;
    // 将屏幕空间坐标归一化到[0,1]范围
    vec2 uv = FRAGCOORD.xy / iResolution.xy;
    
    // 计算当前像素属于哪个频段(0-127)
    int band = int(uv.x * 128.0);
    
    // 确保band在有效范围内
    band = clamp(band, 0, 127);
    
    // 获取对应频段的强度值
    float intensity = spectrum[band] * sensitivity;
    
    // 计算颜色混合因子 (基于频段)
    float color_factor = float(band) / 127.0;
    vec4 band_color;
    
    if (color_factor < 0.5) {
        // 低频到中频的混合
        band_color = mix(color_low, color_mid, color_factor * 2.0);
    } else {
        // 中频到高频的混合
        band_color = mix(color_mid, color_high, (color_factor - 0.5) * 2.0);
    }
    
    // 翻转y坐标，使频谱从底部开始向上显示
    float flipped_y = 1.0 - uv.y;
    
    // 检查当前像素是否在频谱柱内
    if (flipped_y < intensity) {
        // 在频谱柱内，根据高度调整亮度
        float height_factor = flipped_y / intensity;
        COLOR = mix(band_color * 0.5, band_color, height_factor);
    } else {
        // 在频谱柱外，显示背景色
        COLOR = bg_color;
    }
}