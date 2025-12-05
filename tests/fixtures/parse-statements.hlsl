float4 main(float4 pos : POSITION0) : COLOR0
{
    float4 acc = pos;
    if (pos.x > 0)
        acc = acc + 1;
    else
        acc = acc - 1;

    for (int i = 0; i < 4; i = i + 1)
    {
        acc = acc + 1;
    }

    while (acc.x < 10)
        acc = acc + 1;

    do
        acc = acc - 1;
    while (acc.x > 0);

    return acc;
}
