shader_type canvas_item;

// 从C#传递的频谱数据
uniform float spectrum[64];

// 音频影响控制参数
uniform float audio_strength : hint_range(0.0, 1.0) = 0.5;
uniform float color_intensity : hint_range(0.0, 1.0) = 0.5;

uniform sampler2D screen_texture : hint_screen_texture;

const float REPEAT = 5.0;

// 旋转矩阵
mat2 rot(float a) {
	float c = cos(a), s = sin(a);
	return mat2(vec2(c, s), vec2(-s, c));
}

float sdBox(vec3 p, vec3 b) {
	vec3 q = abs(p) - b;
	return length(max(q, 0.0)) + min(max(q.x, max(q.y, q.z)), 0.0);
}

float box(vec3 pos, float scale) {
	pos *= scale;
	float base = sdBox(pos, vec3(.4, .4, .1)) / 1.5;
	pos.xy *= 5.;
	pos.y -= 3.5;
	pos.xy *= rot(.75);
	float result = -base;
	return result;
}

float box_set(vec3 pos, float iTime) {
	vec3 pos_origin = pos;
	pos = pos_origin;

	// 使用音频数据影响位置 - 降低强度
	float audio_influence = spectrum[int(mod(iTime * 5.0, 63.0))] * 2.0 * audio_strength;

	// 低音时减少图案
	float bass = spectrum[0] * 0.5;
	float bass_reduction = 1.0 - bass;

	pos.y += sin(iTime * 0.4) * 2.5 * (1.0 + audio_influence * 0.5) * bass_reduction;
	pos.xy *= rot(.8);
	float box1 = box(pos, 2.0 - abs(sin(iTime * 0.4)) * 1.5 * (1.0 + audio_influence * 0.3) * bass_reduction);

	pos = pos_origin;
	pos.y -= sin(iTime * 0.4) * 2.5 * (1.0 + audio_influence * 0.5) * bass_reduction;
	pos.xy *= rot(.8);
	float box2 = box(pos, 2.0 - abs(sin(iTime * 0.4)) * 1.5 * (1.0 + audio_influence * 0.3) * bass_reduction);

	pos = pos_origin;
	pos.x += sin(iTime * 0.4) * 2.5 * (1.0 + audio_influence * 0.5) * bass_reduction;
	pos.xy *= rot(.8);
	float box3 = box(pos, 2.0 - abs(sin(iTime * 0.4)) * 1.5 * (1.0 + audio_influence * 0.3) * bass_reduction);

	pos = pos_origin;
	pos.x -= sin(iTime * 0.4) * 2.5 * (1.0 + audio_influence * 0.5) * bass_reduction;
	pos.xy *= rot(.8);
	float box4 = box(pos, 2.0 - abs(sin(iTime * 0.4)) * 1.5 * (1.0 + audio_influence * 0.3) * bass_reduction);

	pos = pos_origin;
	pos.xy *= rot(.8);

	// 中高音时增加盒子大小，但降低影响
	float mid = spectrum[32] * 0.3;
	float high = spectrum[63] * 0.4;
	float box5_size = 0.5 + (mid + high) * 0.2 * audio_strength;
	float box5 = box(pos, box5_size) * 6.0;

	pos = pos_origin;
	float box6_size = 0.5 + (mid + high) * 0.2 * audio_strength;
	float box6 = box(pos, box6_size) * 6.0;

	float result = max(max(max(max(max(box1, box2), box3), box4), box5), box6);
	return result;
}

float map(vec3 pos, float iTime) {
	vec3 pos_origin = pos;
	float box_set1 = box_set(pos, iTime);
	return box_set1;
}

void fragment() {
	vec2 iResolution = 1.0 / SCREEN_PIXEL_SIZE;
	vec2 p = (FRAGCOORD.xy * 2.0 - iResolution.xy) / min(iResolution.x, iResolution.y);

	// 使用音频数据轻微影响相机位置
	float bass = spectrum[10] * 0.3;
	float mid = spectrum[32] * 0.4;
	float high = spectrum[63] * 0.5;

	vec3 ro = vec3(
		sin(TIME * 0.2) * (mid + high) * 0.2,
		-0.2 + cos(TIME * 0.3) * (mid + high) * 0.1,
		TIME * 4.0 - bass * 1.0
	);

	vec3 ray = normalize(vec3(p, 1.5));

	// 使用音频数据轻微影响旋转
	//float audio_rot_mid = mid * 1.5;
	//float audio_rot_high = high * 2.0;
	//float audio_rot = (audio_rot_mid + audio_rot_high) * 0.5 * audio_strength;
	//ray.xy = ray.xy * rot(sin(TIME * 0.03) * 3.0 + audio_rot);
	//ray.yz = ray.yz * rot(sin(TIME * 0.05) * 0.1 + audio_rot * 0.05);

	float t = 0.1;
	vec3 col = vec3(0.0);
	float ac = 0.0;

	for (int i = 0; i < 99; i++) {
		vec3 pos = ro + ray * t;
		pos = mod(pos - 2.0, 4.0) - 2.0;
		float gTime = TIME - float(i) * 0.01;

		float d = map(pos, TIME);
		d = max(abs(d), 0.01);

		// 使用音频数据增强视觉效果 - 降低强度
		float audio_boost_mid = spectrum[32 + int(mod(float(i) * 0.3, 32.0))] * 0.8;
		float audio_boost_high = spectrum[48 + int(mod(float(i) * 0.5, 16.0))] * 1.0;
		float audio_boost = (audio_boost_mid + audio_boost_high) * 0.5 * audio_strength;

		// 低音时减少积累效果
		float bass_reduction = 1.0 - (bass * 0.1);
		ac += exp(-d * (20.0 + audio_boost)) * bass_reduction;

		t += d * (0.5 + audio_boost * 0.02);
	}

	col = vec3(ac * 0.015);

	// 使用音频数据轻微影响颜色 - 更加柔和
	col += vec3(
		bass * 0.3 * color_intensity,
		mid * 1.0 * abs(sin(TIME * 0.5)) * color_intensity,
		0.3 + high * 1.5 * sin(TIME * 0.5) * 0.1 * color_intensity
	);

	// 添加一些平滑处理，减少闪烁
	col = mix(col, texture(screen_texture, SCREEN_UV).rgb, 0.1);

	COLOR = vec4(col, 1.0 - t * (0.01 + 0.01 * sin(TIME * 0.5)));
}