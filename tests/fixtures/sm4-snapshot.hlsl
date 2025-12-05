cbuffer Globals : register(b0)
{
    float4x4 World;
    float4 Tint;
};

Texture2D<float4> Diffuse : register(t0);
SamplerState LinearClamp : register(s0);
RWTexture2D<float4> Target : register(u0);
StructuredBuffer<float4> Data : register(t1);
RWStructuredBuffer<float4> OutData : register(u1);
ByteAddressBuffer Raw : register(t2);

float4 main(float2 uv : TEXCOORD0) : SV_Target
{
    float4 color = Diffuse.Sample(LinearClamp, uv);
    float4 rw = Target[uint2(uv * 256)];
    return color + rw + OutData[0] + asfloat(Raw.Load(0));
}
