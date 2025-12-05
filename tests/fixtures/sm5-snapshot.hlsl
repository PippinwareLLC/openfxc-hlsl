cbuffer PerFrame : register(b0)
{
    float4x4 ViewProj;
};

StructuredBuffer<float3> Positions : register(t0);
RWTexture2D<float4> Output : register(u0);
ByteAddressBuffer Data : register(t1);

float4 VS(uint id : SV_VertexID) : SV_Position
{
    float3 pos = Positions[id];
    return float4(pos, 1.0);
}

float4 PS() : SV_Target0
{
    return Output[uint2(0, 0)];
}
