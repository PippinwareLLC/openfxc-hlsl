float3 BadFunction(float3 P, float u)
{
    // Missing semicolon should produce a diagnostic mapped to this file.
    float3 Pi = floor(P + u)
    return Pi;
}
