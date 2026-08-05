using System.Net;
using System.Net.Http.Json;
using HomeInventory.Client;
using Microsoft.AspNetCore.Mvc.Testing;

namespace HomeInventory.Tests;

public sealed class InventoryApiTests
{
    [Fact]
    public async Task CreateProperty_ReturnsCreatedAndPersists()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = CreateClient(factory);

        var response = await client.PostAsJsonAsync("/api/properties", new PropertyInput(
            "Main House",
            "123 Main St",
            new DateOnly(2024, 1, 1),
            250000m,
            2000m,
            "Primary residence"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<PropertyDto>();
        Assert.NotNull(created);
        Assert.Equal("Main House", created.Name);
        Assert.Equal("123 Main St", created.Address);

        var listResponse = await client.GetAsync("/api/properties");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var properties = await listResponse.Content.ReadFromJsonAsync<List<PropertyDto>>();
        Assert.NotNull(properties);
        Assert.Single(properties);
        Assert.Equal(created.Id, properties[0].Id);
    }

    [Fact]
    public async Task CreateRoom_WithValidProperty_ReturnsCreated()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = CreateClient(factory);

        var propertyResponse = await client.PostAsJsonAsync("/api/properties", new PropertyInput("Vacation House", null, null, null, null, null));
        var property = await propertyResponse.Content.ReadFromJsonAsync<PropertyDto>();
        Assert.NotNull(property);

        var floorResponse = await client.PostAsJsonAsync($"/api/properties/{property.Id}/floors", new FloorInput(property.Id, "Main Floor", null));
        var floor = await floorResponse.Content.ReadFromJsonAsync<FloorDto>();
        Assert.NotNull(floor);

        var roomResponse = await client.PostAsJsonAsync("/api/rooms", new RoomInput(
            floor.Id,
            "Kitchen",
            "Kitchen",
            10m,
            20m,
            3m,
            4m,
            5m,
            6m,
            "Tile",
            "Paint",
            "Drywall",
            "Light blue",
            1,
            1,
            "Sink",
            "Oven",
            "Kitchen notes"));

        Assert.Equal(HttpStatusCode.Created, roomResponse.StatusCode);
        var room = await roomResponse.Content.ReadFromJsonAsync<RoomDto>();
        Assert.NotNull(room);
        Assert.Equal(floor.Id, room.FloorId);
        Assert.Equal("Kitchen", room.Name);

        var listResponse = await client.GetAsync($"/api/rooms?floorId={floor.Id}");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var rooms = await listResponse.Content.ReadFromJsonAsync<List<RoomDto>>();
        Assert.NotNull(rooms);
        Assert.Single(rooms);
        Assert.Equal(room.Id, rooms[0].Id);
    }

    [Fact]
    public async Task CreateAsset_AndArchive_UpdatesState()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = CreateClient(factory);

        var propertyResponse = await client.PostAsJsonAsync("/api/properties", new PropertyInput("Rental", null, null, null, null, null));
        var property = await propertyResponse.Content.ReadFromJsonAsync<PropertyDto>();
        Assert.NotNull(property);

        var assetResponse = await client.PostAsJsonAsync("/api/assets", new AssetInput(
            property.Id,
            null,
            null,
            "Couch",
            "Furniture",
            "Living room sofa",
            "Acme",
            "Model 1",
            "ABC123",
            new DateOnly(2024, 1, 2),
            500m,
            450m,
            "Good",
            "Comfortable"));

        Assert.Equal(HttpStatusCode.Created, assetResponse.StatusCode);
        var asset = await assetResponse.Content.ReadFromJsonAsync<AssetDto>();
        Assert.NotNull(asset);
        Assert.False(asset.IsArchived);

        var archiveResponse = await client.PostAsync($"/api/assets/{asset.Id}/archive", null);
        Assert.Equal(HttpStatusCode.NoContent, archiveResponse.StatusCode);

        var archivedResponse = await client.GetAsync("/api/assets?archived=true");
        Assert.Equal(HttpStatusCode.OK, archivedResponse.StatusCode);
        var archivedAssets = await archivedResponse.Content.ReadFromJsonAsync<List<AssetDto>>();
        Assert.NotNull(archivedAssets);
        Assert.Single(archivedAssets);
        Assert.Equal(asset.Id, archivedAssets[0].Id);
    }

    [Fact]
    public async Task ImportPreviewAndConfirm_IncludesPropertyPhotos()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = CreateClient(factory);

        var import = new InventoryExport(
            1,
            [new ImportProperty("prop-1", "Main House", "123 Main St", null, null, null, null)],
            [],
            [],
            [],
            [],
            [],
            [new ImportPropertyPhoto("photo-1", "prop-1", "photos/main-house/front-door.jpg", "Front door", 1)]);

        var previewResponse = await client.PostAsJsonAsync("/api/import/preview", import);
        Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
        var preview = await previewResponse.Content.ReadFromJsonAsync<ImportPreviewDto>();
        Assert.NotNull(preview);
        Assert.True(preview.IsValid);

        var confirmResponse = await client.PostAsJsonAsync("/api/import/confirm", new { inventory = import, skipExternalIds = new List<string>() });
        Assert.Equal(HttpStatusCode.NoContent, confirmResponse.StatusCode);

        var propertyResponse = await client.GetAsync("/api/properties");
        Assert.Equal(HttpStatusCode.OK, propertyResponse.StatusCode);
        var properties = await propertyResponse.Content.ReadFromJsonAsync<List<PropertyDto>>();
        Assert.NotNull(properties);
        Assert.Single(properties);

        var photosResponse = await client.GetAsync($"/api/properties/{properties[0].Id}/photos");
        Assert.Equal(HttpStatusCode.OK, photosResponse.StatusCode);
        var photos = await photosResponse.Content.ReadFromJsonAsync<List<PhotoMetadataDto>>();
        Assert.NotNull(photos);
        Assert.Single(photos);
        Assert.Equal("Front door", photos[0].Caption);
    }

    private static HttpClient CreateClient(CustomWebApplicationFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
}
