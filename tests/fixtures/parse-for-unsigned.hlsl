float foo(float start)
{
    float acc = 0;
    for (unsigned int i = 0; i < 4; i++)
    {
        acc += start + i;
    }
    return acc;
}
