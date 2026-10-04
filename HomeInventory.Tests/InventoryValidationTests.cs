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

    [Fact]
    public async Task ExportThenImport_RoundTripsPaintsAndAllPhotos()
    {
        InventoryExport backup;
        using (var sourceFactory = new CustomWebApplicationFactory())
        using (var source = CreateClient(sourceFactory))
        {
            var property = await CreatePropertyAsync(source);
            var floor = await CreateFloorAsync(source, property.Id);
            var room = await CreateRoomAsync(source, floor.Id);
            var fixture = await (await source.PostAsJsonAsync($"/api/rooms/{room.Id}/fixtures", FixtureFor(room.Id))).Content.ReadFromJsonAsync<FixtureDto>();
            Assert.NotNull(fixture);
            Assert.Equal(HttpStatusCode.Created, (await source.PostAsJsonAsync($"/api/fixtures/{fixture.Id}/photos", new FixturePhotoInput("fixtures/sink.jpg", "Sink", 0))).StatusCode);
            Assert.Equal(HttpStatusCode.Created, (await source.PostAsJsonAsync($"/api/rooms/{room.Id}/photos", new PhotoMetadataInput("rooms/lounge.jpg", "Lounge", 0))).StatusCode);
            var paint = await (await source.PostAsJsonAsync("/api/paints", new PaintInput("Dulux", "Oyster White", "OW-104", "Eggshell", null))).Content.ReadFromJsonAsync<PaintDto>();
            Assert.NotNull(paint);
            Assert.Equal(HttpStatusCode.OK, (await source.PostAsJsonAsync($"/api/rooms/{room.Id}/paints", new RoomPaintInput(paint.Id, 0, "North wall"))).StatusCode);
            await CreateAssetAsync(source, property.Id, room.Id, null, "Couch");
            var archived = await CreateAssetAsync(source, property.Id, null, null, "Old TV");
            Assert.Equal(HttpStatusCode.Created, (await source.PostAsJsonAsync($"/api/assets/{archived.Id}/photos", new AssetPhotoInput("assets/tv.jpg", null, 0))).StatusCode);
            await source.PostAsync($"/api/assets/{archived.Id}/archive", null);

            backup = (await source.GetFromJsonAsync<InventoryExport>("/api/export"))!;
        }

        Assert.Single(backup.Paints!);
        Assert.Single(backup.RoomPaints!);
        Assert.Single(backup.RoomPhotos!);
        Assert.Single(backup.FixturePhotos!);
        Assert.Empty(backup.AssetPhotos!);

        using var targetFactory = new CustomWebApplicationFactory();
        using var target = CreateClient(targetFactory);
        var preview = await (await target.PostAsJsonAsync("/api/import/preview", backup)).Content.ReadFromJsonAsync<ImportPreviewDto>();
        Assert.NotNull(preview);
        Assert.True(preview.IsValid, string.Join("; ", preview.Errors));
        Assert.Equal(1, preview.Paints);
        Assert.Equal(2, preview.Photos);
        Assert.Equal(HttpStatusCode.NoContent, (await target.PostAsJsonAsync("/api/import/confirm", new { inventory = backup, skipExternalIds = new List<string>() })).StatusCode);

        var restored = await target.GetFromJsonAsync<InventoryExport>("/api/export");
        Assert.NotNull(restored);
        var restoredRoomPaint = Assert.Single(restored.RoomPaints!);
        Assert.Equal("North wall", restoredRoomPaint.Surface);
        Assert.Equal("Oyster White", Assert.Single(restored.Paints!).ColorName);
        Assert.Equal("Lounge", Assert.Single(restored.RoomPhotos!).Caption);
        Assert.Equal("Sink", Assert.Single(restored.FixturePhotos!).Caption);
        Assert.Single(restored.Assets);

        // Importing the same backup again reuses the existing paint rather than duplicating the library.
        var secondPreview = await (await target.PostAsJsonAsync("/api/import/preview", backup)).Content.ReadFromJsonAsync<ImportPreviewDto>();
        Assert.NotNull(secondPreview);
        Assert.Equal(HttpStatusCode.NoContent, (await target.PostAsJsonAsync("/api/import/confirm", new { inventory = backup, skipExternalIds = secondPreview.DuplicateExternalIds })).StatusCode);
        var paints = await target.GetFromJsonAsync<List<PaintDto>>("/api/paints");
        Assert.NotNull(paints);
        Assert.Single(paints);
    }

    [Fact]
    public async Task ImportPreview_DetectsDuplicatesByExternalId()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = CreateClient(factory);
        InventoryExport ImportOf(params ImportAsset[] assets) =>
            new(1, [new ImportProperty("prop-1", "Main House", null, null, null, null, null)], [], [], [], [], [.. assets], []);
        ImportAsset Asset(string externalId, string name) => new(externalId, "prop-1", null, null, name, "Tools", null, null, null, null, null, null, null, null, null);

        var first = ImportOf(Asset("asset-1", "Drill"));
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/import/confirm", new { inventory = first, skipExternalIds = new List<string>() })).StatusCode);

        // Same external ID under a new name is a duplicate; a different ID with the same name is a new asset.
        var second = ImportOf(Asset("asset-1", "Cordless drill"), Asset("asset-2", "Drill"));
        var preview = await (await client.PostAsJsonAsync("/api/import/preview", second)).Content.ReadFromJsonAsync<ImportPreviewDto>();
        Assert.NotNull(preview);
        Assert.Equal(["asset-1"], preview.DuplicateExternalIds);

        // Exports keep the imported external ID so backup → restore → backup cycles stay recognisable.
        var export = await client.GetFromJsonAsync<InventoryExport>("/api/export");
        Assert.NotNull(export);
        Assert.Equal("asset-1", Assert.Single(export.Assets).ExternalId);
    }

    [Fact]
    public async Task Assets_ArchiveAndUnarchive_NoHardDelete()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = CreateClient(factory);
        var property = await CreatePropertyAsync(client);
        var asset = await CreateAssetAsync(client, property.Id, null, null, "Couch");

        Assert.False((await client.DeleteAsync($"/api/assets/{asset.Id}")).IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/assets/{asset.Id}/archive", null)).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<List<AssetDto>>("/api/assets?archived=false"))!);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/assets/{asset.Id}/unarchive", null)).StatusCode);
        var active = await client.GetFromJsonAsync<List<AssetDto>>("/api/assets?archived=false");
        Assert.NotNull(active);
        Assert.False(Assert.Single(active).IsArchived);
    }

    [Fact]
    public async Task Dashboard_TotalsEachCurrencySeparately()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = CreateClient(factory);
        var euroHome = await (await client.PostAsJsonAsync("/api/properties", new PropertyInput("Paris flat", null, null, null, null, null, "EUR"))).Content.ReadFromJsonAsync<PropertyDto>();
        var randHome = await (await client.PostAsJsonAsync("/api/properties", new PropertyInput("Cape Town house", null, null, null, null, null, "ZAR"))).Content.ReadFromJsonAsync<PropertyDto>();
        Assert.NotNull(euroHome);
        Assert.NotNull(randHome);
        await client.PostAsJsonAsync("/api/assets", new AssetInput(euroHome.Id, null, null, "TV", "Electronics", null, null, null, null, null, null, 800m, null, null));
        await client.PostAsJsonAsync("/api/assets", new AssetInput(randHome.Id, null, null, "Sofa", "Furniture", null, null, null, null, null, null, 15000m, null, null));
        var floor = await CreateFloorAsync(client, randHome.Id);
        var room = await CreateRoomAsync(client, floor.Id);
        await client.PostAsJsonAsync($"/api/rooms/{room.Id}/fixtures", FixtureFor(room.Id) with { CurrentValue = 2000m });

        var dashboard = await client.GetFromJsonAsync<DashboardDto>("/api/dashboard");

        Assert.NotNull(dashboard);
        Assert.Equal(2, dashboard.AssetCount);
        Assert.Equal(1, dashboard.FixtureCount);
        var zar = Assert.Single(dashboard.Totals, x => x.Currency == "ZAR");
        Assert.Equal(17000m, zar.TotalValue);
        Assert.Equal(2000m, zar.FixtureValue);
        var eur = Assert.Single(dashboard.Totals, x => x.Currency == "EUR");
        Assert.Equal(800m, eur.TotalValue);
        Assert.Equal("Electronics", Assert.Single(eur.Categories).Category);
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
