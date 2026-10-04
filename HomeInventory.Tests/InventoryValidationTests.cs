using System.Net;
using System.Net.Http.Json;
using HomeInventory.Client;
using Microsoft.AspNetCore.Mvc.Testing;

namespace HomeInventory.Tests;

public sealed class InventoryValidationTests
{
    [Fact]
    public async Task StorageLocations_CreateNested_ComputesPathAndDeletesLeafFirst()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = CreateClient(factory);
        var property = await CreatePropertyAsync(client);

        var garage = await CreateLocationAsync(client, property.Id, null, "Garage");
        var shelf = await CreateLocationAsync(client, property.Id, garage.Id, "Shelf B");
        Assert.Equal("Garage → Shelf B", shelf.Path);

        var deleteParentResponse = await client.DeleteAsync($"/api/locations/{garage.Id}");
        Assert.Equal(HttpStatusCode.BadRequest, deleteParentResponse.StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/locations/{shelf.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/locations/{garage.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/locations/{garage.Id}")).StatusCode);

        var locations = await client.GetFromJsonAsync<List<StorageLocationDto>>("/api/locations");
        Assert.NotNull(locations);
        Assert.Empty(locations);
    }

    [Fact]
    public async Task StorageLocation_Delete_WithAsset_ReturnsBadRequest()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = CreateClient(factory);
        var property = await CreatePropertyAsync(client);
        var box = await CreateLocationAsync(client, property.Id, null, "Box 12");
        await CreateAssetAsync(client, property.Id, null, box.Id, "Passport scanner");

        var response = await client.DeleteAsync($"/api/locations/{box.Id}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task StorageLocation_Update_RejectsCycle()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = CreateClient(factory);
        var property = await CreatePropertyAsync(client);
        var garage = await CreateLocationAsync(client, property.Id, null, "Garage");
        var shelf = await CreateLocationAsync(client, property.Id, garage.Id, "Shelf");
        var drawer = await CreateLocationAsync(client, property.Id, shelf.Id, "Drawer");

        var response = await client.PutAsJsonAsync($"/api/locations/{garage.Id}", new StorageLocationInput(property.Id, drawer.Id, "Garage", null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var listResponse = await client.GetAsync("/api/locations");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
    }

    [Fact]
    public async Task StorageLocation_Update_RejectsPropertyChangeWithChildren()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = CreateClient(factory);
        var home = await CreatePropertyAsync(client, "Home");
        var cottage = await CreatePropertyAsync(client, "Cottage");
        var garage = await CreateLocationAsync(client, home.Id, null, "Garage");
        await CreateLocationAsync(client, home.Id, garage.Id, "Shelf");

        var response = await client.PutAsJsonAsync($"/api/locations/{garage.Id}", new StorageLocationInput(cottage.Id, null, "Garage", null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DeleteRoomAndFloor_WithAssets_ReturnsBadRequestUntilAssetsMoved()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = CreateClient(factory);
        var property = await CreatePropertyAsync(client);
        var floor = await CreateFloorAsync(client, property.Id);
        var room = await CreateRoomAsync(client, floor.Id);
        var asset = await CreateAssetAsync(client, property.Id, room.Id, null, "Couch");

        Assert.Equal(HttpStatusCode.BadRequest, (await client.DeleteAsync($"/api/rooms/{room.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.DeleteAsync($"/api/floors/{floor.Id}")).StatusCode);

        var moveResponse = await client.PostAsJsonAsync($"/api/assets/{asset.Id}/move", new AssetMoveInput(null, null));
        Assert.Equal(HttpStatusCode.NoContent, moveResponse.StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/floors/{floor.Id}")).StatusCode);
        var rooms = await client.GetFromJsonAsync<List<RoomDto>>("/api/rooms");
        Assert.NotNull(rooms);
        Assert.Empty(rooms);
    }

    [Fact]
    public async Task UpdateFixture_WithUnknownRoom_ReturnsBadRequest()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = CreateClient(factory);
        var property = await CreatePropertyAsync(client);
        var floor = await CreateFloorAsync(client, property.Id);
        var room = await CreateRoomAsync(client, floor.Id);
        var fixtureResponse = await client.PostAsJsonAsync($"/api/rooms/{room.Id}/fixtures", FixtureFor(room.Id));
        var fixture = await fixtureResponse.Content.ReadFromJsonAsync<FixtureDto>();
        Assert.NotNull(fixture);

        var response = await client.PutAsJsonAsync($"/api/fixtures/{fixture.Id}", FixtureFor(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ImportConfirm_SkippedDuplicateAssetWithPhotos_Succeeds()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = CreateClient(factory);
        var import = new InventoryExport(
            1,
            [new ImportProperty("prop-1", "Main House", null, null, null, null, null)],
            [],
            [],
            [],
            [],
            [new ImportAsset("asset-1", "prop-1", null, null, "Drill", "Tools", null, null, null, null, null, null, null, null, null)],
            [],
            [],
            [new ImportAssetPhoto("asset-photo-1", "asset-1", "assets/drill.jpg", "Drill", 0)]);

        var firstConfirm = await client.PostAsJsonAsync("/api/import/confirm", new { inventory = import, skipExternalIds = new List<string>() });
        Assert.Equal(HttpStatusCode.NoContent, firstConfirm.StatusCode);

        var preview = await (await client.PostAsJsonAsync("/api/import/preview", import)).Content.ReadFromJsonAsync<ImportPreviewDto>();
        Assert.NotNull(preview);
        Assert.Equal(["asset-1"], preview.DuplicateExternalIds);

        var secondConfirm = await client.PostAsJsonAsync("/api/import/confirm", new { inventory = import, skipExternalIds = preview.DuplicateExternalIds });
        Assert.Equal(HttpStatusCode.NoContent, secondConfirm.StatusCode);

        var assets = await client.GetFromJsonAsync<List<AssetDto>>("/api/assets?archived=false");
        Assert.NotNull(assets);
        Assert.Single(assets);
    }

    private static FixtureInput FixtureFor(Guid roomId) =>
        new(roomId, "Sink", "Sink", null, null, null, null, null, null, null, null, null, null, null, null, null, null);

    private static async Task<PropertyDto> CreatePropertyAsync(HttpClient client, string name = "Main House")
    {
        var response = await client.PostAsJsonAsync("/api/properties", new PropertyInput(name, null, null, null, null, null));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PropertyDto>())!;
    }

    private static async Task<FloorDto> CreateFloorAsync(HttpClient client, Guid propertyId)
    {
        var response = await client.PostAsJsonAsync($"/api/properties/{propertyId}/floors", new FloorInput(propertyId, "Ground Floor", null));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<FloorDto>())!;
    }

    private static async Task<RoomDto> CreateRoomAsync(HttpClient client, Guid floorId)
    {
        var response = await client.PostAsJsonAsync("/api/rooms", new RoomInput(floorId, "Lounge", null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<RoomDto>())!;
    }

    private static async Task<StorageLocationDto> CreateLocationAsync(HttpClient client, Guid propertyId, Guid? parentId, string name)
    {
        var response = await client.PostAsJsonAsync("/api/locations", new StorageLocationInput(propertyId, parentId, name, null));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<StorageLocationDto>())!;
    }

    private static async Task<AssetDto> CreateAssetAsync(HttpClient client, Guid propertyId, Guid? roomId, Guid? locationId, string name)
    {
        var response = await client.PostAsJsonAsync("/api/assets", new AssetInput(propertyId, roomId, locationId, name, "General", null, null, null, null, null, null, null, null, null));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AssetDto>())!;
    }

    private static HttpClient CreateClient(CustomWebApplicationFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
}
