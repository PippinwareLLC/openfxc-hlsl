sampler2D s0 : register(s0);

sampler_state LinearSampler
{
    MinFilter = Linear;
    MagFilter = Linear;
};
