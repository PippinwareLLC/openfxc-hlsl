VertexShader ShellVS = asm
{
    vs.1.1
    dcl_position v0
    mov oPos, v0
};

float4 main(float4 pos : POSITION) : POSITION
{
    return pos;
}
