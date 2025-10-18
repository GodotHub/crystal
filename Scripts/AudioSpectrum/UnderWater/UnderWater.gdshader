shader_type canvas_item;

uniform vec3 skycolor : source_color = vec3(0.0, 0.039, 0.196);
uniform float bubble_speed : hint_range(0.1, 2.0) = 1.0; // 气泡速度控制
uniform int bubble_count : hint_range(1, 10) = 5; // 气泡数量控制

// 用代码生成的噪声函数替代纹理采样
float hash(vec2 p) {
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

float noise(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    vec2 u = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash(i + vec2(0.0, 0.0)), 
                   hash(i + vec2(1.0, 0.0)), u.x),
               mix(hash(i + vec2(0.0, 1.0)), 
                   hash(i + vec2(1.0, 1.0)), u.x), u.y);
}

// 替代纹理采样的噪声函数
vec3 texture_noise(vec2 uv) {
    float n = noise(uv * 10.0);
    return vec3(n, noise(uv * 10.0 + 100.0), noise(uv * 10.0 + 200.0));
}

#define TAU 6.28318530718
#define MAX_ITER 5

float speck(vec2 pos, vec2 uv, float radius) {
    pos.y += 0.05;
    float color = distance(pos, uv);
    vec3 tex = texture_noise(sin(vec2(uv) * 10.1));
    vec3 tex2 = texture_noise(sin(vec2(pos) * 10.1));
    color = clamp((1.0 - pow(color * (5.0 / radius), pow(radius, 0.9))), 0.0, 1.0);
    color *= clamp(mix(sin(tex.y) + 0.1, cos(tex.x), 0.5) * sin(tex2.x) + 0.2, 0.0, 1.0);
    return color;
}

vec3 caustic(vec2 uv) {
    vec2 p = mod(uv * TAU, TAU) - 250.0;
    float time = TIME * 0.5 + 23.0;

    vec2 i = vec2(p);
    float c = 1.0;
    float inten = 0.005;

    for (int n = 0; n < MAX_ITER; n++) {
        float t = time * (1.0 - (3.5 / float(n + 1)));
        i = p + vec2(cos(t - i.x) + sin(t + i.y), sin(t - i.y) + cos(t + i.x));
        c += 1.0 / length(vec2(p.x / (sin(i.x + t) / inten), p.y / (cos(i.y + t) / inten)));
    }
    
    c /= float(MAX_ITER);
    c = 1.17 - pow(c, 1.4);
    vec3 color = vec3(pow(abs(c), 8.0));
    color = clamp(color + vec3(0.0, 0.35, 0.5), 0.0, 1.0);
    color = mix(color, vec3(1.0, 1.0, 1.0), 0.3);
    
    return color;
}

float causticX(float x, float power, float gtime) {
    float p = mod(x * TAU, TAU) - 250.0;
    float time = gtime * 0.5 + 23.0;

    float i = p;
    float c = 1.0;
    float inten = 0.005;

    for (int n = 0; n < MAX_ITER / 2; n++) {
        float t = time * (1.0 - (3.5 / float(n + 1)));
        i = p + cos(t - i) + sin(t + i);
        c += 1.0 / length(p / (sin(i + t) / inten));
    }
    c /= float(MAX_ITER);
    c = 1.17 - pow(c, power);
    
    return c;
}

float GodRays(vec2 uv) {
    float light = 0.0;

    light += pow(causticX((uv.x + 0.08 * uv.y) / 1.7 + 0.5, 1.8, TIME * 0.65), 10.0) * 0.05;
    light -= pow((1.0 - uv.y) * 0.3, 2.0) * 0.2;
    light += pow(causticX(sin(uv.x), 0.3, TIME * 0.7), 9.0) * 0.4; 
    light += pow(causticX(cos(uv.x * 2.3), 0.3, TIME * 1.3), 4.0) * 0.1;  
        
    light -= pow((1.0 - uv.y) * 0.3, 3.0);
    light = clamp(light, 0.0, 1.0);
    
    return light;
}


void fragment() {
    vec2 fragCoord = FRAGCOORD.xy;
    vec2 iResolution = 1.0 / SCREEN_PIXEL_SIZE;
    
    vec2 p = (-iResolution.xy + 2.0 * fragCoord.xy) / iResolution.y;
    vec3 color = skycolor;
    float sky = clamp(0.8 * (1.0 - 0.8 * p.y), 0.0, 1.0);
    color = sky * skycolor;
    
    // 添加焦散效果
    color += ((0.3 * caustic(vec2(p.x, p.y * 1.0))) + (0.3 * caustic(vec2(p.x, p.y * 2.7)))) * pow(p.y, 4.0);
    
    // 添加气泡效果
    vec3 colorBubble = vec3(0.0);
    float bubble = 0.0;
    
    // 根据气泡数量参数控制显示的气泡
    if (bubble_count >= 1)
        bubble += speck(vec2(sin(TIME * 0.32 * bubble_speed), cos(TIME * bubble_speed) * 0.2 + 0.1), p, -0.08);
    if (bubble_count >= 2)
        bubble += speck(vec2(sin(1.0 - TIME * 0.39 * bubble_speed) + 0.5, cos(1.0 - TIME * 0.69 * bubble_speed) * 0.2 + 0.15), p, 0.07);   
    if (bubble_count >= 3)
        bubble += speck(vec2(cos(1.0 - TIME * 0.5 * bubble_speed) - 0.5, sin(1.0 - TIME * 0.36 * bubble_speed) * 0.2 + 0.1), p, 0.12);   
    if (bubble_count >= 4)
        bubble += speck(vec2(sin(TIME * 0.44 * bubble_speed) - 1.0, cos(1.0 - TIME * 0.32 * bubble_speed) * 0.2 + 0.15), p, -0.09);   
    if (bubble_count >= 5)
        bubble += speck(vec2(1.0 - sin(1.0 - TIME * 0.6 * bubble_speed) - 1.3, sin(1.0 - TIME * 0.82 * bubble_speed) * 0.2 + 0.1), p, 0.15);
    if (bubble_count >= 6)
        bubble += speck(vec2(sin(TIME * 0.5 * bubble_speed) + 0.7, cos(TIME * 0.45 * bubble_speed) * 0.2 + 0.2), p, 0.1);
    if (bubble_count >= 7)
        bubble += speck(vec2(cos(TIME * 0.55 * bubble_speed) - 0.8, sin(TIME * 0.4 * bubble_speed) * 0.2 + 0.15), p, -0.07);
    if (bubble_count >= 8)
        bubble += speck(vec2(sin(TIME * 0.65 * bubble_speed) + 1.0, cos(TIME * 0.35 * bubble_speed) * 0.2 + 0.12), p, 0.09);
    if (bubble_count >= 9)
        bubble += speck(vec2(cos(TIME * 0.6 * bubble_speed) - 1.2, sin(TIME * 0.5 * bubble_speed) * 0.2 + 0.18), p, 0.11);
    if (bubble_count >= 10)
        bubble += speck(vec2(sin(TIME * 0.7 * bubble_speed) + 1.5, cos(TIME * 0.55 * bubble_speed) * 0.2 + 0.14), p, -0.1);
    
    colorBubble = bubble * vec3(0.2, 0.7, 1.0);
    
    // 添加上帝光效果
    float godRaysEffect = GodRays(p);
    color += godRaysEffect * mix(skycolor, vec3(1.0), p.y * p.y) * vec3(0.7, 1.0, 1.0);
    
    // 添加气泡
    color += colorBubble;
    
    // 伽马校正
    vec3 gamma = vec3(0.46);
    COLOR = vec4(pow(color, gamma), 1.0);
}