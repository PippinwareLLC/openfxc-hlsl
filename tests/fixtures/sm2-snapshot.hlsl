float4x4 WorldViewProj;

sampler2D Diffuse : register(s0);

float4 main(float4 pos : POSITION0, float2 uv : TEXCOORD0) : COLOR0
{
    float4 world = mul(pos, WorldViewProj);
    return world + tex2D(Diffuse, uv);
}
