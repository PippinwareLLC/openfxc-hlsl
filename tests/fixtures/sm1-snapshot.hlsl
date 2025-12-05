sampler2D Diffuse : register(s0);

sampler_state LinearSampler
{
    MinFilter = Linear;
    MagFilter = Linear;
};

float4 main(float4 pos : POSITION) : COLOR0
{
    return tex2D(Diffuse, pos.xy);
}
