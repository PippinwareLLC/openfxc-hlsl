struct VertexInput
{
    float4 pos : POSITION0;
    float2 uv : TEXCOORD0;
};

class SimpleClass
{
    float4 value;
};

interface IExample
{
    void DoIt();
};

cbuffer Globals : register(b0)
{
    float4x4 World;
};

Texture2D<float4> Diffuse;
SamplerState Linear;
VertexInput inputs[4];
