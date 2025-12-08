float4 main(float3 v : POSITION) : SV_Target
{
    float a = v.x > 0.5f ? v.x : 0.0f;
    float b = v.y > 0.0f ? v.y : -v.y;
    return float4(a, b, 0.0f, 1.0f);
}
