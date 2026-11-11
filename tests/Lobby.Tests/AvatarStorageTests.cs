using System.Text;
using Lobby.Web.Services;
using Microsoft.AspNetCore.Http;

namespace Lobby.Tests;

public class AvatarStorageTests : IDisposable
{
    private static readonly byte[] PngHeader = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D, 0x49, 0x48, 0x44, 0x52];
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "lobby-avatars-" + Guid.NewGuid().ToString("N"));
    private readonly AvatarStorage _storage;

    public AvatarStorageTests() => _storage = new AvatarStorage(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static FormFile File(byte[] content, string fileName, string contentType) =>
        new(new MemoryStream(content), 0, content.Length, "avatar", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType,
        };

    [Fact]
    public async Task Png_extension_with_non_png_bytes_is_rejected()
    {
        var result = await _storage.SaveAsync(File(Encoding.UTF8.GetBytes("<html><script>alert(1)</script></html>"), "cat.png", "image/png"));

        Assert.Null(result.FileName);
        Assert.NotNull(result.Error);
        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task Oversized_file_is_rejected()
    {
        var content = new byte[AvatarStorage.MaxBytes + 1];
        PngHeader.CopyTo(content, 0);

        var result = await _storage.SaveAsync(File(content, "big.png", "image/png"));

        Assert.Null(result.FileName);
        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task Svg_is_rejected()
    {
        var svg = Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>");

        var result = await _storage.SaveAsync(File(svg, "logo.svg", "image/svg+xml"));

        Assert.Null(result.FileName);
        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task Content_type_mismatch_is_rejected()
    {
        var result = await _storage.SaveAsync(File(PngHeader, "cat.png", "text/html"));

        Assert.Null(result.FileName);
    }

    [Fact]
    public async Task Valid_png_is_stored_under_a_guid_name_and_the_original_name_is_not_used()
    {
        var result = await _storage.SaveAsync(File(PngHeader, "../../minha-foto secreta.png", "image/png"));

        Assert.Null(result.Error);
        Assert.Matches("^[0-9a-f]{32}\\.png$", result.FileName);
        var stored = Assert.Single(Directory.GetFiles(_directory));
        Assert.Equal(result.FileName, Path.GetFileName(stored));
        Assert.DoesNotContain("secreta", stored);
    }

    [Theory]
    [InlineData("../keys/key.xml")]
    [InlineData("..%2Fsecret.png")]
    [InlineData("0123456789abcdef0123456789abcdef.svg")]
    [InlineData("0123456789ABCDEF0123456789ABCDEF.png")]
    [InlineData("foto.png")]
    public void Avatar_endpoint_rejects_names_we_did_not_generate(string fileName)
    {
        Assert.False(AvatarStorage.IsValidFileName(fileName));
        Assert.Null(_storage.GetPath(fileName));
    }
}

public class DisplayTests
{
    [Theory]
    [InlineData("Ana Souza", "AS")]
    [InlineData("Clube do livro (demo)", "CL")]
    [InlineData("ana", "A")]
    [InlineData("  ", "?")]
    public void Initials_use_first_and_last_word_that_start_with_a_letter(string name, string expected) =>
        Assert.Equal(expected, Lobby.Web.Services.Display.Initials(name));
}
