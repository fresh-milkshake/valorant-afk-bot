using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using ImGuiNET;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.GLControl;
using Keys = System.Windows.Forms.Keys;
using MouseButtons = System.Windows.Forms.MouseButtons;

namespace ValorantAfkBot.App.ImGuiSupport;

public sealed class ImGuiController : IDisposable
{
    private readonly List<char> _pressedChars = [];
    private readonly HashSet<Keys> _keysDown = [];

    private int _vertexArray;
    private int _vertexBuffer;
    private int _indexBuffer;
    private int _vertexBufferSize = 10_000;
    private int _indexBufferSize = 2_000;
    private int _fontTexture;
    private int _shader;
    private int _shaderProjectionMatrixLocation;
    private int _shaderTextureLocation;

    private bool _frameBegun;
    private System.Numerics.Vector2 _scaleFactor = System.Numerics.Vector2.One;

    public ImGuiController(int width, int height)
    {
        IntPtr context = ImGui.CreateContext();
        ImGui.SetCurrentContext(context);

        ImGuiIOPtr io = ImGui.GetIO();
        io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;
        io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;
        io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;
        io.DisplaySize = new System.Numerics.Vector2(width, height);
        io.Fonts.AddFontDefault();
        ImGui.StyleColorsDark();

        CreateDeviceResources();

        ImGui.NewFrame();
        _frameBegun = true;
    }

    public void Bind(GLControl control)
    {
        control.KeyDown += OnKeyDown;
        control.KeyUp += OnKeyUp;
        control.KeyPress += OnKeyPress;
        control.MouseWheel += OnMouseWheel;
    }

    public void Update(GLControl control, float deltaSeconds)
    {
        if (_frameBegun)
        {
            ImGui.Render();
        }

        SetPerFrameImGuiData(control.ClientSize.Width, control.ClientSize.Height, deltaSeconds);
        UpdateInput(control);

        _frameBegun = true;
        ImGui.NewFrame();
    }

    public void Render()
    {
        if (!_frameBegun)
        {
            return;
        }

        _frameBegun = false;
        ImGui.Render();
        RenderImDrawData(ImGui.GetDrawData());
    }

    public void WindowResized(int width, int height)
    {
        ImGui.GetIO().DisplaySize = new System.Numerics.Vector2(width, height);
    }

    public void Dispose()
    {
        if (_vertexBuffer != 0)
        {
            GL.DeleteBuffer(_vertexBuffer);
        }

        if (_indexBuffer != 0)
        {
            GL.DeleteBuffer(_indexBuffer);
        }

        if (_vertexArray != 0)
        {
            GL.DeleteVertexArray(_vertexArray);
        }

        if (_fontTexture != 0)
        {
            GL.DeleteTexture(_fontTexture);
        }

        if (_shader != 0)
        {
            GL.DeleteProgram(_shader);
        }
    }

    private void SetPerFrameImGuiData(int width, int height, float deltaSeconds)
    {
        ImGuiIOPtr io = ImGui.GetIO();
        io.DisplaySize = new System.Numerics.Vector2(width / _scaleFactor.X, height / _scaleFactor.Y);
        io.DisplayFramebufferScale = _scaleFactor;
        io.DeltaTime = deltaSeconds <= 0 ? 1f / 60f : deltaSeconds;
    }

    private void UpdateInput(GLControl control)
    {
        ImGuiIOPtr io = ImGui.GetIO();

        Point point = control.PointToClient(Cursor.Position);
        io.AddMousePosEvent(point.X, point.Y);
        io.AddMouseButtonEvent(0, (Control.MouseButtons & MouseButtons.Left) != 0);
        io.AddMouseButtonEvent(1, (Control.MouseButtons & MouseButtons.Right) != 0);
        io.AddMouseButtonEvent(2, (Control.MouseButtons & MouseButtons.Middle) != 0);

        io.AddKeyEvent(ImGuiKey.ModCtrl, (Control.ModifierKeys & Keys.Control) != 0);
        io.AddKeyEvent(ImGuiKey.ModAlt, (Control.ModifierKeys & Keys.Alt) != 0);
        io.AddKeyEvent(ImGuiKey.ModShift, (Control.ModifierKeys & Keys.Shift) != 0);
        io.AddKeyEvent(ImGuiKey.ModSuper, (Control.ModifierKeys & Keys.LWin) != 0 || (Control.ModifierKeys & Keys.RWin) != 0);

        foreach (char character in _pressedChars)
        {
            io.AddInputCharacter(character);
        }

        _pressedChars.Clear();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_keysDown.Add(e.KeyCode))
        {
            ImGui.GetIO().AddKeyEvent(MapKey(e.KeyCode), true);
        }
    }

    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        _keysDown.Remove(e.KeyCode);
        ImGui.GetIO().AddKeyEvent(MapKey(e.KeyCode), false);
    }

    private void OnKeyPress(object? sender, KeyPressEventArgs e)
    {
        if (!char.IsControl(e.KeyChar))
        {
            _pressedChars.Add(e.KeyChar);
        }
    }

    private void OnMouseWheel(object? sender, MouseEventArgs e)
    {
        float wheelY = e.Delta > 0 ? 1f : -1f;
        ImGui.GetIO().AddMouseWheelEvent(0f, wheelY);
    }

    private unsafe void CreateDeviceResources()
    {
        _vertexBuffer = GL.GenBuffer();
        _indexBuffer = GL.GenBuffer();
        _vertexArray = GL.GenVertexArray();

        RecreateFontDeviceTexture();

        const string vertexSource = """
            #version 330 core
            uniform mat4 projection_matrix;
            layout (location = 0) in vec2 in_position;
            layout (location = 1) in vec2 in_texCoord;
            layout (location = 2) in vec4 in_color;
            out vec2 frag_texCoord;
            out vec4 frag_color;
            void main()
            {
                frag_texCoord = in_texCoord;
                frag_color = in_color;
                gl_Position = projection_matrix * vec4(in_position, 0, 1);
            }
            """;

        const string fragmentSource = """
            #version 330 core
            uniform sampler2D in_fontTexture;
            in vec2 frag_texCoord;
            in vec4 frag_color;
            out vec4 outputColor;
            void main()
            {
                outputColor = frag_color * texture(in_fontTexture, frag_texCoord);
            }
            """;

        int vertex = GL.CreateShader(ShaderType.VertexShader);
        GL.ShaderSource(vertex, vertexSource);
        GL.CompileShader(vertex);
        CheckShader(vertex);

        int fragment = GL.CreateShader(ShaderType.FragmentShader);
        GL.ShaderSource(fragment, fragmentSource);
        GL.CompileShader(fragment);
        CheckShader(fragment);

        _shader = GL.CreateProgram();
        GL.AttachShader(_shader, vertex);
        GL.AttachShader(_shader, fragment);
        GL.LinkProgram(_shader);
        GL.GetProgram(_shader, GetProgramParameterName.LinkStatus, out int status);
        if (status == 0)
        {
            throw new InvalidOperationException(GL.GetProgramInfoLog(_shader));
        }

        GL.DetachShader(_shader, vertex);
        GL.DetachShader(_shader, fragment);
        GL.DeleteShader(vertex);
        GL.DeleteShader(fragment);

        _shaderProjectionMatrixLocation = GL.GetUniformLocation(_shader, "projection_matrix");
        _shaderTextureLocation = GL.GetUniformLocation(_shader, "in_fontTexture");

        GL.BindVertexArray(_vertexArray);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vertexBuffer);
        GL.BufferData(BufferTarget.ArrayBuffer, _vertexBufferSize, nint.Zero, BufferUsageHint.DynamicDraw);
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, _indexBuffer);
        GL.BufferData(BufferTarget.ElementArrayBuffer, _indexBufferSize, nint.Zero, BufferUsageHint.DynamicDraw);

        int stride = Unsafe.SizeOf<ImDrawVert>();
        GL.EnableVertexAttribArray(0);
        GL.EnableVertexAttribArray(1);
        GL.EnableVertexAttribArray(2);
        GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, stride, 0);
        GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, stride, 8);
        GL.VertexAttribPointer(2, 4, VertexAttribPointerType.UnsignedByte, true, stride, 16);

        GL.BindVertexArray(0);
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, 0);
    }

    private unsafe void RecreateFontDeviceTexture()
    {
        ImGuiIOPtr io = ImGui.GetIO();
        io.Fonts.GetTexDataAsRGBA32(out byte* pixels, out int width, out int height, out _);

        _fontTexture = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, _fontTexture);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.PixelStore(PixelStoreParameter.UnpackRowLength, 0);
        GL.TexImage2D(
            TextureTarget.Texture2D,
            0,
            PixelInternalFormat.Rgba,
            width,
            height,
            0,
            PixelFormat.Rgba,
            PixelType.UnsignedByte,
            (nint)pixels);

        io.Fonts.SetTexID((nint)_fontTexture);
        io.Fonts.ClearTexData();
    }

    private static void CheckShader(int shader)
    {
        GL.GetShader(shader, ShaderParameter.CompileStatus, out int status);
        if (status == 0)
        {
            throw new InvalidOperationException(GL.GetShaderInfoLog(shader));
        }
    }

    private unsafe void RenderImDrawData(ImDrawDataPtr drawData)
    {
        int framebufferWidth = (int)(drawData.DisplaySize.X * drawData.FramebufferScale.X);
        int framebufferHeight = (int)(drawData.DisplaySize.Y * drawData.FramebufferScale.Y);
        if (framebufferWidth <= 0 || framebufferHeight <= 0)
        {
            return;
        }

        drawData.ScaleClipRects(ImGui.GetIO().DisplayFramebufferScale);

        GL.Viewport(0, 0, framebufferWidth, framebufferHeight);
        GL.Enable(EnableCap.Blend);
        GL.BlendEquation(BlendEquationMode.FuncAdd);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        GL.Disable(EnableCap.CullFace);
        GL.Disable(EnableCap.DepthTest);
        GL.Enable(EnableCap.ScissorTest);

        Matrix4 projection = Matrix4.CreateOrthographicOffCenter(
            drawData.DisplayPos.X,
            drawData.DisplayPos.X + drawData.DisplaySize.X,
            drawData.DisplayPos.Y + drawData.DisplaySize.Y,
            drawData.DisplayPos.Y,
            -1f,
            1f);

        GL.UseProgram(_shader);
        GL.Uniform1(_shaderTextureLocation, 0);
        GL.UniformMatrix4(_shaderProjectionMatrixLocation, false, ref projection);
        GL.BindVertexArray(_vertexArray);

        for (int commandListIndex = 0; commandListIndex < drawData.CmdListsCount; commandListIndex++)
        {
            ImDrawListPtr commandList = drawData.CmdLists[commandListIndex];

            int vertexSize = commandList.VtxBuffer.Size * Unsafe.SizeOf<ImDrawVert>();
            if (vertexSize > _vertexBufferSize)
            {
                while (vertexSize > _vertexBufferSize)
                {
                    _vertexBufferSize *= 2;
                }

                GL.BindBuffer(BufferTarget.ArrayBuffer, _vertexBuffer);
                GL.BufferData(BufferTarget.ArrayBuffer, _vertexBufferSize, nint.Zero, BufferUsageHint.DynamicDraw);
            }

            int indexSize = commandList.IdxBuffer.Size * sizeof(ushort);
            if (indexSize > _indexBufferSize)
            {
                while (indexSize > _indexBufferSize)
                {
                    _indexBufferSize *= 2;
                }

                GL.BindBuffer(BufferTarget.ElementArrayBuffer, _indexBuffer);
                GL.BufferData(BufferTarget.ElementArrayBuffer, _indexBufferSize, nint.Zero, BufferUsageHint.DynamicDraw);
            }

            GL.BindBuffer(BufferTarget.ArrayBuffer, _vertexBuffer);
            GL.BufferSubData(BufferTarget.ArrayBuffer, nint.Zero, vertexSize, (nint)commandList.VtxBuffer.Data);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, _indexBuffer);
            GL.BufferSubData(BufferTarget.ElementArrayBuffer, nint.Zero, indexSize, (nint)commandList.IdxBuffer.Data);

            int indexOffset = 0;
            for (int commandIndex = 0; commandIndex < commandList.CmdBuffer.Size; commandIndex++)
            {
                ImDrawCmdPtr command = commandList.CmdBuffer[commandIndex];
                if (command.UserCallback != nint.Zero)
                {
                    throw new NotSupportedException("ImGui user callbacks are not supported.");
                }

                GL.BindTexture(TextureTarget.Texture2D, (int)command.TextureId);
                System.Numerics.Vector4 clip = command.ClipRect;
                GL.Scissor(
                    (int)clip.X,
                    framebufferHeight - (int)clip.W,
                    (int)(clip.Z - clip.X),
                    (int)(clip.W - clip.Y));

                if ((ImGui.GetIO().BackendFlags & ImGuiBackendFlags.RendererHasVtxOffset) != 0)
                {
                    GL.DrawElementsBaseVertex(
                        PrimitiveType.Triangles,
                        (int)command.ElemCount,
                        DrawElementsType.UnsignedShort,
                        (nint)(indexOffset * sizeof(ushort)),
                        (int)command.VtxOffset);
                }
                else
                {
                    GL.DrawElements(
                        PrimitiveType.Triangles,
                        (int)command.ElemCount,
                        DrawElementsType.UnsignedShort,
                        indexOffset * sizeof(ushort));
                }

                indexOffset += (int)command.ElemCount;
            }
        }

        GL.Disable(EnableCap.ScissorTest);
        GL.BindVertexArray(0);
        GL.UseProgram(0);
    }
    private static ImGuiKey MapKey(Keys key) => key switch
    {
        Keys.Tab => ImGuiKey.Tab,
        Keys.Left => ImGuiKey.LeftArrow,
        Keys.Right => ImGuiKey.RightArrow,
        Keys.Up => ImGuiKey.UpArrow,
        Keys.Down => ImGuiKey.DownArrow,
        Keys.PageUp => ImGuiKey.PageUp,
        Keys.PageDown => ImGuiKey.PageDown,
        Keys.Home => ImGuiKey.Home,
        Keys.End => ImGuiKey.End,
        Keys.Insert => ImGuiKey.Insert,
        Keys.Delete => ImGuiKey.Delete,
        Keys.Back => ImGuiKey.Backspace,
        Keys.Space => ImGuiKey.Space,
        Keys.Enter => ImGuiKey.Enter,
        Keys.Escape => ImGuiKey.Escape,
        Keys.A => ImGuiKey.A,
        Keys.C => ImGuiKey.C,
        Keys.V => ImGuiKey.V,
        Keys.X => ImGuiKey.X,
        Keys.Y => ImGuiKey.Y,
        Keys.Z => ImGuiKey.Z,
        Keys.W => ImGuiKey.W,
        Keys.S => ImGuiKey.S,
        Keys.D => ImGuiKey.D,
        Keys.F9 => ImGuiKey.F9,
        Keys.F10 => ImGuiKey.F10,
        Keys.F11 => ImGuiKey.F11,
        _ => ImGuiKey.None,
    };
}
