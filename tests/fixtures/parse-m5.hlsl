typedef float4 Color;

typedef struct VS_INPUT
{
    float4 pos : POSITION0;
    float2 uv : TEXCOORD0;
} VS_INPUT_ALIAS;

typedef Texture2D<float4> Tex2;

class Material
{
    float4 Diffuse;
    float4 Shade(float2 uv) : COLOR0;
};

interface IProcess
{
    void Execute(float4 pos : POSITION0) : POSITION0;
};

SamplerState Linear : register(s0);
Texture2D<float4> Diffuse : register(t0);

cbuffer Globals : register(b0)
{
    float4x4 World;
};

VS_INPUT inputs[4];
