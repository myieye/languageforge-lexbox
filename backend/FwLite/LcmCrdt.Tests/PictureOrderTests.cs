using MiniLcm.Media;
using MiniLcm.SyncHelpers;

namespace LcmCrdt.Tests;

public class PictureOrderTests : IAsyncLifetime
{
    private readonly MiniLcmApiFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();
    public Task DisposeAsync() => _fixture.DisposeAsync();

    private static Picture NewPicture() => new() { Id = Guid.NewGuid(), MediaUri = MediaUri.NotFound };

    [Fact]
    public async Task CreatePicture_WithBetween_PlacesPictureBetweenNeighbors()
    {
        var entry = await _fixture.Api.CreateEntry(new Entry { LexemeForm = { ["en"] = "test" } });
        var sense = await _fixture.Api.CreateSense(entry.Id, new Sense { Id = Guid.NewGuid() });

        var pictureA = await _fixture.Api.CreatePicture(entry.Id, sense.Id, NewPicture());
        var pictureC = await _fixture.Api.CreatePicture(entry.Id, sense.Id, NewPicture());
        var pictureB = await _fixture.Api.CreatePicture(entry.Id, sense.Id, NewPicture(),
            new BetweenPosition(pictureA.Id, pictureC.Id));

        var result = await _fixture.Api.GetSense(entry.Id, sense.Id);
        result.Should().NotBeNull();
        result!.Pictures.OrderBy(p => p.Order).Select(p => p.Id)
            .Should().Equal(pictureA.Id, pictureB.Id, pictureC.Id);
        new[] { pictureA.Order, pictureB.Order, pictureC.Order }.Should().OnlyHaveUniqueItems();
    }
}
