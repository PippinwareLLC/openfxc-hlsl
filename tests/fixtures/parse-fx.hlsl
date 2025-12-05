technique10 Render
{
    pass P0
    {
        SetVertexShader( CompileShader( vs_4_0, VSMain() ) );
        SetPixelShader( CompileShader( ps_4_0, PSMain() ) );
    }
}
