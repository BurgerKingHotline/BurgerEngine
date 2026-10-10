namespace Burger.Runtime;

/// <summary>GLSL 330 shaders for the map.</summary>
internal static class Shaders
{
    public const string TerrainVertex = @"#version 330
in vec3 vertexPosition;
in vec2 vertexTexCoord;
in vec3 vertexNormal;
uniform mat4 mvp;
uniform mat4 matModel;
uniform mat4 matNormal;
out vec2 fragTexCoord;
out vec3 fragNormal;
out vec3 fragPos;
void main()
{
    fragTexCoord = vertexTexCoord;
    fragNormal = normalize(vec3(matNormal * vec4(vertexNormal, 0.0)));
    fragPos = vec3(matModel * vec4(vertexPosition, 1.0));
    gl_Position = mvp * vec4(vertexPosition, 1.0);
}
";

    public const string TerrainFragment = @"#version 330
in vec2 fragTexCoord;
in vec3 fragNormal;
in vec3 fragPos;

uniform sampler2D texture0; // map data: RG = location index + 1, B = terrain index, A = land: river (255) / water: closeness to the coast
uniform sampler2D texture1; // per-location lookup: block0 color, block1 owner/state ids, block2 stripe color + flags
uniform sampler2D texture2; // terrain texture atlas
uniform vec4 colDiffuse;
uniform vec2 mapSize;
uniform vec2 worldSize;
uniform vec2 lutSize;
uniform float blockRows;
uniform float atlasGrid;
uniform float tileScale;
uniform vec3 waterColor;
uniform vec3 riverColor;
uniform float time;
uniform float borderScale;
uniform float showProvinceBorders;
uniform float tintStrength;
uniform float fogStrength;   // 0 when zoomed far out (strategic view)   // how strongly map mode colors cover the terrain (stronger when zoomed out)
uniform vec3 viewPos;

out vec4 finalColor;

// flags (lut block 2 alpha): 1 water, 2 occupied stripes, 4 selected, 8 hovered, 16 lake, 32 hidden by fog of war, 64 highlighted state

ivec2 clampPix(ivec2 p) { return clamp(p, ivec2(0), ivec2(mapSize) - ivec2(1)); }

int locAt(ivec2 p)
{
    vec4 c = texelFetch(texture0, clampPix(p), 0);
    return int(c.r * 255.0 + 0.5) + int(c.g * 255.0 + 0.5) * 256 - 1;
}

vec4 lut(int idx, int block)
{
    if (idx < 0) return vec4(0.0);
    int w = int(lutSize.x);
    ivec2 p = ivec2(idx - (idx / w) * w, idx / w + block * int(blockRows));
    return texelFetch(texture1, p, 0);
}

int ownerOf(int idx) { vec4 v = lut(idx, 1); return int(v.r * 255.0 + 0.5) + int(v.g * 255.0 + 0.5) * 256; }
int stateOf(int idx) { vec4 v = lut(idx, 1); return int(v.b * 255.0 + 0.5) + int(v.a * 255.0 + 0.5) * 256; }
int flagsOf(int idx) { if (idx < 0) return 1; return int(lut(idx, 2).a * 255.0 + 0.5); }

float terrainAt(ivec2 p) { return floor(texelFetch(texture0, clampPix(p), 0).b * 255.0 + 0.5); }

vec3 atlas(float t, vec2 uv)
{
    vec2 tile = vec2(mod(t, atlasGrid), floor(t / atlasGrid));
    vec2 local = fract(uv) * 0.96 + 0.02;
    vec2 g = uv * 0.96 / atlasGrid;
    return textureGrad(texture2, (tile + local) / atlasGrid, dFdx(g), dFdy(g)).rgb;
}

// hash without sine (stable on every GPU)
float hash(vec2 p) { vec3 p3 = fract(vec3(p.xyx) * 0.1031); p3 += dot(p3, p3.yzx + 33.33); return fract((p3.x + p3.y) * p3.z); }
float noise(vec2 p)
{
    vec2 i = floor(p), f = fract(p);
    vec2 u = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash(i), hash(i + vec2(1, 0)), u.x), mix(hash(i + vec2(0, 1)), hash(i + vec2(1, 1)), u.x), u.y);
}
float waves(vec2 p)
{
    return noise(p * 0.9 + vec2(time * 0.05, time * 0.03)) * 0.6 + noise(p * 2.3 - vec2(time * 0.07, -time * 0.04)) * 0.4;
}

// Distance in screen pixels from a smoothed border: m0..m3 say whether each of the 4 nearest map pixels
// belongs to the same region as this fragment. The bilinear blend of those masks crosses 0.5 on a smooth
// curve instead of the pixel staircase, so borders look drawn rather than pixelated.
float edgePx(float m0, float m1, float m2, float m3, vec2 f, float texelsPerPixel)
{
    float w = mix(mix(m0, m1, f.x), mix(m2, m3, f.x), f.y);
    if (w > 0.999) return 1e4;
    vec2 g = vec2((1.0 - f.y) * (m1 - m0) + f.y * (m3 - m2), (1.0 - f.x) * (m2 - m0) + f.x * (m3 - m1));
    return max(w - 0.5, 0.0) / max(length(g), 0.05) / texelsPerPixel;
}
float line(float d, float halfWidth) { return 1.0 - smoothstep(halfWidth - 0.7, halfWidth + 0.7, d); }

void main()
{
    vec2 uv = fragPos.xz / worldSize;
    vec2 pix = uv * mapSize;
    float tpp = max(max(fwidth(pix.x), fwidth(pix.y)), 1e-4); // map pixels per screen pixel

    // the 4 map pixels around this fragment
    vec2 pp = pix - 0.5;
    ivec2 b = ivec2(floor(pp));
    vec2 f = fract(pp);
    ivec2 o4[4] = ivec2[4](ivec2(0, 0), ivec2(1, 0), ivec2(0, 1), ivec2(1, 1));
    float wt[4] = float[4]((1.0 - f.x) * (1.0 - f.y), f.x * (1.0 - f.y), (1.0 - f.x) * f.y, f.x * f.y);
    int L[4]; int O[4]; int ST[4]; int WA[4];
    for (int k = 0; k < 4; k++)
    {
        L[k] = locAt(b + o4[k]);
        O[k] = ownerOf(L[k]);
        ST[k] = stateOf(L[k]);
        WA[k] = flagsOf(L[k]) & 1;
    }
    // this fragment belongs to the location with the most weight (smooth outlines instead of pixel steps)
    int best = 0; float bestW = -1.0;
    for (int k = 0; k < 4; k++)
    {
        float s = 0.0;
        for (int j = 0; j < 4; j++) if (L[j] == L[k]) s += wt[j];
        if (s > bestW) { bestW = s; best = k; }
    }
    int loc = L[best];
    int owner = O[best];
    int st = ST[best];
    int flags = flagsOf(loc);
    // outside the map (the endless sea): open water, fogged like the map edge next to it
    bool outside = pix.x < 0.0 || pix.y < 0.0 || pix.x > mapSize.x || pix.y > mapSize.y;
    if (outside) flags = 1 | (flags & 32);
    bool water = (flags & 1) != 0;

    float mProv[4]; float mCoast[4]; float mCountry[4]; float mState[4];
    for (int k = 0; k < 4; k++)
    {
        mProv[k] = L[k] == loc ? 1.0 : 0.0;
        mCoast[k] = WA[k] == WA[best] ? 1.0 : 0.0;
        bool otherLand = WA[k] == 0;
        mCountry[k] = (otherLand && O[k] != owner) ? 0.0 : 1.0;
        mState[k] = (otherLand && ST[k] != st) ? 0.0 : 1.0;
    }
    float dProv = edgePx(mProv[0], mProv[1], mProv[2], mProv[3], f, tpp);
    float dCoast = edgePx(mCoast[0], mCoast[1], mCoast[2], mCoast[3], f, tpp);
    float dCountry = water ? 1e4 : edgePx(mCountry[0], mCountry[1], mCountry[2], mCountry[3], f, tpp);
    float dState = water ? 1e4 : edgePx(mState[0], mState[1], mState[2], mState[3], f, tpp);
    if (outside) { dProv = 1e4; dCoast = 1e4; }

    // zoomed far out several map pixels share one screen pixel: look further away so borders stay visible
    float far = smoothstep(0.6, 1.4, tpp);
    if (far > 0.0 && !outside)
    {
        ivec2 ip = ivec2(floor(pix));
        ivec2 offs[4] = ivec2[4](ivec2(1, 0), ivec2(-1, 0), ivec2(0, 1), ivec2(0, -1));
        int d = max(1, int(tpp + 0.5));
        for (int k = 0; k < 4; k++)
        {
            int o = locAt(ip + offs[k] * d);
            if (o == loc) continue;
            dProv = min(dProv, 0.5);
            bool otherWater = (flagsOf(o) & 1) != 0;
            if (water != otherWater) dCoast = min(dCoast, 0.5);
            else if (!water && ownerOf(o) != owner) dCountry = min(dCountry, 0.5);
            else if (!water && stateOf(o) != st) dState = min(dState, 0.5);
        }
    }

    vec3 sunDir = normalize(vec3(-0.35, 1.0, -0.45));
    vec3 viewDir = normalize(viewPos - fragPos);
    vec4 data = texelFetch(texture0, clampPix(ivec2(floor(pix))), 0);
    float detail = 1.0 - smoothstep(0.02, 0.12, fwidth(fragPos.x)); // fades small details when zoomed out

    vec3 col;
    if (water)
    {
        // deep water is dark blue, water near the coast lighter and greener
        float shallow = outside ? 0.0 : clamp(data.a * 255.0 / 240.0, 0.0, 1.0);
        shallow = shallow * shallow;
        vec3 deep = waterColor * 0.72;
        vec3 shore = mix(waterColor, vec3(0.30, 0.62, 0.66), 0.55) * 1.15;
        col = mix(deep, shore, shallow);
        // gentle waves: normal from the slope of a smooth noise field
        vec2 wp = fragPos.xz;
        float e = 0.08;
        float h0 = waves(wp), hx = waves(wp + vec2(e, 0.0)), hz = waves(wp + vec2(0.0, e));
        vec3 wn = normalize(vec3((h0 - hx) / e * 0.22 * detail, 1.0, (h0 - hz) / e * 0.22 * detail));
        col *= 0.95 + 0.07 * h0;
        float fres = pow(1.0 - max(dot(wn, viewDir), 0.0), 3.0);
        col = mix(col, vec3(0.55, 0.68, 0.80), fres * 0.35);
        float spec = pow(max(dot(reflect(-sunDir, wn), viewDir), 0.0), 90.0);
        col += vec3(1.0, 0.95, 0.85) * spec * 0.22 * detail;
        if ((flags & 16) != 0) col = mix(col, shore, 0.4);
        // light foam line along the coast
        col = mix(col, vec3(0.80, 0.86, 0.84), line(dCoast, 1.2) * 0.45);
    }
    else
    {
        // blend the four nearest terrain texels so terrain types fade into each other
        vec2 tuv = fragPos.xz * tileScale;
        vec3 c00 = atlas(terrainAt(b), tuv);
        vec3 c10 = atlas(terrainAt(b + ivec2(1, 0)), tuv);
        vec3 c01 = atlas(terrainAt(b + ivec2(0, 1)), tuv);
        vec3 c11 = atlas(terrainAt(b + ivec2(1, 1)), tuv);
        col = mix(mix(c00, c10, f.x), mix(c01, c11, f.x), f.y);
        // large scale variation hides the texture repeating
        col *= 0.90 + 0.20 * noise(fragPos.xz * 0.11) * (0.5 + 0.5 * noise(fragPos.xz * 0.37));
        if (data.a > 0.5) col = mix(col, riverColor, 0.85);

        // hill shading
        vec3 n = normalize(fragNormal);
        float ndl = max(dot(n, sunDir), 0.0);
        col *= 0.38 + 0.72 * ndl;
        col = mix(col, col * vec3(1.05, 1.0, 0.92), ndl * 0.3);          // warm light, cool shadows

        // map mode color: multiplied over the terrain so hills and forests still show (HOI4 style)
        vec4 mc = lut(loc, 0);
        if (mc.a > 0.0)
        {
            vec3 tinted = mix(mc.rgb * (0.55 + 0.45 * ndl), col * mc.rgb * 2.1, 0.45);
            col = mix(col, tinted, clamp(mc.a * tintStrength, 0.0, 1.0));
            // a band of the country color just inside its border
            float band = 1.0 - smoothstep(0.0, 9.0 * borderScale, dCountry);
            col = mix(col, mc.rgb * 0.8, band * 0.35 * mc.a);
        }
        if ((flags & 2) != 0)
        {
            // occupied: stripes about 10 pixels wide on screen at any zoom level
            float k = fragPos.x + fragPos.z;
            float period = max(fwidth(k) * 12.0, 0.08);
            float s = smoothstep(0.45, 0.55, fract(k / period));
            col = mix(col, lut(loc, 2).rgb, s * 0.6);
        }
        col = mix(col, vec3(0.06), line(dProv, 0.6) * 0.30 * showProvinceBorders);
        col = mix(col, vec3(0.08), line(dState, 0.9) * 0.55);
        col = mix(col, vec3(0.03), line(dCountry, 1.1 + 0.5 * borderScale) * 0.92);
        col = mix(col, vec3(0.14, 0.12, 0.10), line(dCoast, 0.9) * 0.6);
    }
    if (water) col = mix(col, waterColor * 0.6, line(dProv, 0.6) * 0.18 * showProvinceBorders);

    // fog of war: grey and darker; the sea only a little
    if ((flags & 32) != 0 && fogStrength > 0.0)
    {
        float lum = dot(col, vec3(0.3, 0.59, 0.11));
        vec3 fogged;
        if (water) fogged = mix(col, vec3(lum), 0.35) * 0.82;
        else
        {
            fogged = mix(col, vec3(lum) * vec3(0.78, 0.80, 0.86), 0.72) * 0.62;
            float h = step(0.85, fract((fragPos.x - fragPos.z) / max(fwidth(fragPos.x) * 9.0, 0.05)));
            fogged *= 1.0 - h * 0.10;
        }
        col = mix(col, fogged, fogStrength);
    }

    // highlights: the whole location brightens (land and water alike)
    float pe = line(dProv, 1.4);
    if (outside) { finalColor = vec4(col, 1.0) * colDiffuse; return; }
    if ((flags & 64) != 0) col = mix(mix(col, vec3(1.0, 0.95, 0.8), 0.2), vec3(1.0, 0.82, 0.35), max(line(dState, 1.6), line(dCountry, 1.6)) * 0.85);
    if ((flags & 8) != 0) col = col * 1.18 + vec3(0.05);
    if ((flags & 4) != 0) col = mix(col * 1.25 + vec3(0.06), vec3(1.0, 0.93, 0.7), pe * 0.85);

    finalColor = vec4(col, 1.0) * colDiffuse;
}
";
}
