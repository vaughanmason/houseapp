using System.Net;
using System.Net.Http.Json;
using HomeInventory.Client;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

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
    public async Task ImportSameBackupTwice_IsIdempotent_AndIncrementalBackupAddsOnlyNewRecords()
    {
        InventoryExport backup;
        using (var sourceFactory = new CustomWebApplicationFactory())
        using (var source = CreateClient(sourceFactory))
        {
            var property = await CreatePropertyAsync(source);
            var floor = await CreateFloorAsync(source, property.Id);
            var room = await CreateRoomAsync(source, floor.Id);
            await source.PostAsJsonAsync($"/api/rooms/{room.Id}/surfaces", SurfaceFor(room.Id, "Wall", "wall"));
            var fixture = await (await source.PostAsJsonAsync($"/api/rooms/{room.Id}/fixtures", FixtureFor(room.Id))).Content.ReadFromJsonAsync<FixtureDto>();
            await source.PostAsJsonAsync($"/api/fixtures/{fixture!.Id}/photos", new FixturePhotoInput("sink.jpg", null, 0));
            await source.PostAsJsonAsync($"/api/rooms/{room.Id}/photos", new PhotoMetadataInput("room.jpg", null, 0));
            await source.PostAsJsonAsync($"/api/properties/{property.Id}/photos", new PhotoMetadataInput("front.jpg", null, 0));
            var paint = await (await source.PostAsJsonAsync("/api/paints", new PaintInput("Dulux", "Oyster White", null, null, null))).Content.ReadFromJsonAsync<PaintDto>();
            await source.PostAsJsonAsync($"/api/rooms/{room.Id}/paints", new RoomPaintInput(paint!.Id, 0, null));
            var garage = await CreateLocationAsync(source, property.Id, null, "Garage");
            await CreateLocationAsync(source, property.Id, garage.Id, "Shelf");
            var asset = await CreateAssetAsync(source, property.Id, room.Id, null, "Couch");
            await source.PostAsJsonAsync($"/api/assets/{asset.Id}/photos", new AssetPhotoInput("couch.jpg", null, 0));
            backup = (await source.GetFromJsonAsync<InventoryExport>("/api/export"))!;
        }

        using var targetFactory = new CustomWebApplicationFactory();
        using var target = CreateClient(targetFactory);
        Assert.Equal(HttpStatusCode.NoContent, (await target.PostAsJsonAsync("/api/import/confirm", new { inventory = backup, skipExternalIds = new List<string>() })).StatusCode);
        var afterFirst = (await target.GetFromJsonAsync<InventoryExport>("/api/export"))!;

        var preview = (await (await target.PostAsJsonAsync("/api/import/preview", backup)).Content.ReadFromJsonAsync<ImportPreviewDto>())!;
        Assert.Equal(14, preview.ExistingRecords); // every record (including the asset's automatic "Added" event) except the room-paint link, which has no backup ID of its own
        Assert.Equal(HttpStatusCode.NoContent, (await target.PostAsJsonAsync("/api/import/confirm", new { inventory = backup, skipExternalIds = preview.DuplicateExternalIds })).StatusCode);
        var afterSecond = (await target.GetFromJsonAsync<InventoryExport>("/api/export"))!;
        AssertSameIds(afterFirst, afterSecond);
        AssertSameIds(backup, afterSecond); // backup IDs survive the restore

        // A later backup with one extra room only adds that room, under the existing floor and property.
        var newRoom = backup.Rooms[0] with { ExternalId = "room-new", Name = "Study" };
        var incremental = backup with { Rooms = [.. backup.Rooms, newRoom] };
        Assert.Equal(HttpStatusCode.NoContent, (await target.PostAsJsonAsync("/api/import/confirm", new { inventory = incremental, skipExternalIds = new List<string>() })).StatusCode);
        var rooms = await target.GetFromJsonAsync<List<RoomDto>>("/api/rooms");
        Assert.Equal(2, rooms!.Count);
        var properties = await target.GetFromJsonAsync<List<PropertyDto>>("/api/properties");
        Assert.Equal(2, (await target.GetFromJsonAsync<List<RoomDto>>($"/api/rooms?propertyId={Assert.Single(properties!).Id}"))!.Count);
    }

    [Fact]
    public async Task Import_LocationsInAnyOrder_AndRejectsBadParents()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = CreateClient(factory);
        InventoryExport ImportOf(params ImportStorageLocation[] locations) =>
            new(1, [new ImportProperty("prop-1", "Main House", null, null, null, null, null)], [], [], [], [.. locations], [], []);

        var childFirst = ImportOf(new ImportStorageLocation("box", "prop-1", "shelf", "Box", null), new ImportStorageLocation("shelf", "prop-1", "garage", "Shelf", null), new ImportStorageLocation("garage", "prop-1", null, "Garage", null));
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/import/confirm", new { inventory = childFirst, skipExternalIds = new List<string>() })).StatusCode);
        var locations = await client.GetFromJsonAsync<List<StorageLocationDto>>("/api/locations");
        Assert.Contains(locations!, x => x.Path == "Garage → Shelf → Box");

        var missingParent = ImportOf(new ImportStorageLocation("box2", "prop-1", "nowhere", "Box", null));
        var cycle = ImportOf(new ImportStorageLocation("a", "prop-1", "b", "A", null), new ImportStorageLocation("b", "prop-1", "a", "B", null));
        foreach (var bad in new[] { missingParent, cycle })
        {
            var preview = (await (await client.PostAsJsonAsync("/api/import/preview", bad)).Content.ReadFromJsonAsync<ImportPreviewDto>())!;
            Assert.False(preview.IsValid);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/import/confirm", new { inventory = bad, skipExternalIds = new List<string>() })).StatusCode);
        }
    }

    private static void AssertSameIds(InventoryExport expected, InventoryExport actual)
    {
        static IEnumerable<string> Ids(InventoryExport x) => x.Properties.Select(y => y.ExternalId).Concat(x.Floors.Select(y => y.ExternalId)).Concat(x.Rooms.Select(y => y.ExternalId))
            .Concat(x.Surfaces.Select(y => y.ExternalId)).Concat(x.StorageLocations.Select(y => y.ExternalId)).Concat(x.Assets.Select(y => y.ExternalId))
            .Concat(x.PropertyPhotos.Select(y => y.ExternalId)).Concat(x.Fixtures!.Select(y => y.ExternalId)).Concat(x.AssetPhotos!.Select(y => y.ExternalId))
            .Concat(x.Paints!.Select(y => y.ExternalId)).Concat(x.RoomPaints!.Select(y => y.ExternalId)).Concat(x.RoomPhotos!.Select(y => y.ExternalId))
            .Concat(x.FixturePhotos!.Select(y => y.ExternalId)).Order();
        Assert.Equal(Ids(expected), Ids(actual));
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

    [Fact]
    public async Task PaintUsage_AndSearch_FindRoomsUsingPaint()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = CreateClient(factory);
        var property = await CreatePropertyAsync(client);
        var floor = await CreateFloorAsync(client, property.Id);
        var lounge = await CreateRoomAsync(client, floor.Id, "Lounge");
        var hallway = await CreateRoomAsync(client, floor.Id, "Hallway");
        var paint = await (await client.PostAsJsonAsync("/api/paints", new PaintInput("Dulux", "Oyster White", "OW-104", "Eggshell", null))).Content.ReadFromJsonAsync<PaintDto>();
        Assert.NotNull(paint);
        await client.PostAsJsonAsync($"/api/rooms/{lounge.Id}/paints", new RoomPaintInput(paint.Id, 0, "North wall"));
        await client.PostAsJsonAsync($"/api/rooms/{hallway.Id}/surfaces", SurfaceFor(hallway.Id, "East wall", "wall") with { PaintBrand = "Dulux", ColorName = "oyster white" });
        await client.PostAsJsonAsync($"/api/rooms/{hallway.Id}/surfaces", SurfaceFor(hallway.Id, "Floor", "flooring") with { ColorName = "Oyster White", Material = "Oak" });

        var usage = await client.GetFromJsonAsync<List<PaintUsageDto>>($"/api/paints/{paint.Id}/usage");

        Assert.NotNull(usage);
        Assert.Equal(2, usage.Count);
        Assert.Contains(usage, x => x.RoomId == lounge.Id && x.Source == "Assigned" && x.Surface == "North wall");
        Assert.Contains(usage, x => x.RoomId == hallway.Id && x.Source == "Surface" && x.Surface == "East wall");

        var results = await client.GetFromJsonAsync<List<SearchResultDto>>("/api/search?q=oyster");
        Assert.NotNull(results);
        var paintResult = Assert.Single(results, x => x.Kind == "Paint");
        Assert.Contains("Lounge", paintResult.LocationPath);
        Assert.Contains("Hallway", paintResult.LocationPath);
        Assert.Equal(2, results.Count(x => x.Kind == "Surface"));
        Assert.Contains(await client.GetFromJsonAsync<List<SearchResultDto>>("/api/search?q=oak") ?? [], x => x.Kind == "Surface" && x.Title == "Floor");
    }

    [Fact]
    public async Task UpdateSurface_ChangesFields()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = CreateClient(factory);
        var property = await CreatePropertyAsync(client);
        var floor = await CreateFloorAsync(client, property.Id);
        var room = await CreateRoomAsync(client, floor.Id);
        var surface = await (await client.PostAsJsonAsync($"/api/rooms/{room.Id}/surfaces", SurfaceFor(room.Id, "Wall", "wall"))).Content.ReadFromJsonAsync<SurfaceDto>();
        Assert.NotNull(surface);

        var response = await client.PutAsJsonAsync($"/api/surfaces/{surface.Id}", SurfaceFor(room.Id, "North wall", "wall") with { PaintBrand = "Plascon", Coats = 2, PaintedDate = new DateOnly(2025, 3, 1) });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var surfaces = await client.GetFromJsonAsync<List<SurfaceDto>>($"/api/rooms/{room.Id}/surfaces");
        var updated = Assert.Single(surfaces!);
        Assert.Equal("North wall", updated.Name);
        Assert.Equal("Plascon", updated.PaintBrand);
        Assert.Equal(2, updated.Coats);
        Assert.Equal(new DateOnly(2025, 3, 1), updated.PaintedDate);
    }

    [Fact]
    public async Task DeleteProperty_BlockedWhileItHasAssets()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = CreateClient(factory);
        var property = await CreatePropertyAsync(client);
        var asset = await CreateAssetAsync(client, property.Id, null, null, "Couch");
        await client.PostAsync($"/api/assets/{asset.Id}/archive", null);

        var response = await client.DeleteAsync($"/api/properties/{property.Id}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Single((await client.GetFromJsonAsync<List<PropertyDto>>("/api/properties"))!);
    }

    [Fact]
    public async Task DeleteProperty_CascadesStructureAndNestedStorage()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = CreateClient(factory);
        var property = await CreatePropertyAsync(client, "Old flat");
        var keep = await CreatePropertyAsync(client, "Home");
        var floor = await CreateFloorAsync(client, property.Id);
        var room = await CreateRoomAsync(client, floor.Id);
        await client.PostAsJsonAsync($"/api/rooms/{room.Id}/surfaces", SurfaceFor(room.Id, "Wall", "wall"));
        await client.PostAsJsonAsync($"/api/rooms/{room.Id}/fixtures", FixtureFor(room.Id));
        await client.PostAsJsonAsync($"/api/properties/{property.Id}/photos", new PhotoMetadataInput("front.jpg", null, 0));
        var garage = await CreateLocationAsync(client, property.Id, null, "Garage");
        var shelf = await CreateLocationAsync(client, property.Id, garage.Id, "Shelf");
        await CreateLocationAsync(client, property.Id, shelf.Id, "Box");
        await CreateLocationAsync(client, keep.Id, null, "Loft");

        var response = await client.DeleteAsync($"/api/properties/{property.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("Home", Assert.Single((await client.GetFromJsonAsync<List<PropertyDto>>("/api/properties"))!).Name);
        Assert.Empty((await client.GetFromJsonAsync<List<FloorDto>>("/api/floors"))!);
        Assert.Empty((await client.GetFromJsonAsync<List<RoomDto>>("/api/rooms"))!);
        Assert.Empty((await client.GetFromJsonAsync<List<FixtureDto>>("/api/fixtures"))!);
        Assert.Equal("Loft", Assert.Single((await client.GetFromJsonAsync<List<StorageLocationDto>>("/api/locations"))!).Name);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/properties/{property.Id}")).StatusCode);
    }

    [Fact]
    public async Task Maintenance_CompleteAdvancesScheduleAndRecordsHistory()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = CreateClient(factory);
        var property = await CreatePropertyAsync(client);
        var recurring = await CreateTaskAsync(client, new MaintenanceTaskInput(property.Id, null, "Clean gutters", 6, "Months", new DateOnly(2026, 1, 1), "Gutter Co", 500m, null));
        Assert.Equal("months", recurring.IntervalUnit);

        var completed = await (await client.PostAsJsonAsync($"/api/maintenance/{recurring.Id}/complete", new MaintenanceCompletionInput(new DateOnly(2026, 1, 10), 450m, "Gutter Co", "All clear"))).Content.ReadFromJsonAsync<MaintenanceTaskDto>();
        Assert.Equal(new DateOnly(2026, 7, 10), completed!.DueOn);
        Assert.Equal(new DateOnly(2026, 1, 10), completed.LastCompletedOn);

        // Back-filling an older service adds history without moving the schedule backwards.
        var backfilled = await (await client.PostAsJsonAsync($"/api/maintenance/{recurring.Id}/complete", new MaintenanceCompletionInput(new DateOnly(2025, 7, 2), null, null, null))).Content.ReadFromJsonAsync<MaintenanceTaskDto>();
        Assert.Equal(new DateOnly(2026, 7, 10), backfilled!.DueOn);
        var history = (await client.GetFromJsonAsync<List<MaintenanceRecordDto>>($"/api/maintenance/{recurring.Id}/history"))!;
        Assert.Equal([new DateOnly(2026, 1, 10), new DateOnly(2025, 7, 2)], history.Select(x => x.CompletedOn));
        Assert.Equal(450m, history[0].Cost);

        var oneOff = await CreateTaskAsync(client, new MaintenanceTaskInput(property.Id, null, "Replace geyser element", null, null, new DateOnly(2026, 2, 1), null, null, null));
        var doneOnce = await (await client.PostAsJsonAsync($"/api/maintenance/{oneOff.Id}/complete", new MaintenanceCompletionInput(new DateOnly(2026, 2, 3), null, null, null))).Content.ReadFromJsonAsync<MaintenanceTaskDto>();
        Assert.Null(doneOnce!.DueOn);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/maintenance/{recurring.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/maintenance/{recurring.Id}/history")).StatusCode);
    }

    [Fact]
    public async Task Maintenance_ValidatesInput()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = CreateClient(factory);
        var home = await CreatePropertyAsync(client, "Home");
        var cottage = await CreatePropertyAsync(client, "Cottage");
        var room = await CreateRoomAsync(client, (await CreateFloorAsync(client, cottage.Id)).Id);
        var fixture = await (await client.PostAsJsonAsync($"/api/rooms/{room.Id}/fixtures", FixtureFor(room.Id))).Content.ReadFromJsonAsync<FixtureDto>();

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/maintenance", new MaintenanceTaskInput(home.Id, null, " ", null, null, null, null, null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/maintenance", new MaintenanceTaskInput(home.Id, null, "Paint", 2, "fortnights", null, null, null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/maintenance", new MaintenanceTaskInput(home.Id, null, "Paint", 0, "months", null, null, null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/maintenance", new MaintenanceTaskInput(home.Id, fixture!.Id, "Service", null, null, null, null, null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/maintenance", new MaintenanceTaskInput(cottage.Id, fixture.Id, "Service", null, null, null, null, null, null))).StatusCode);
    }

    [Fact]
    public async Task Dashboard_CountsOverdueAndDueSoonMaintenance()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = CreateClient(factory);
        var property = await CreatePropertyAsync(client);
        var today = DateOnly.FromDateTime(DateTime.Today);
        await CreateTaskAsync(client, new MaintenanceTaskInput(property.Id, null, "Overdue", null, null, today.AddDays(-1), null, null, null));
        await CreateTaskAsync(client, new MaintenanceTaskInput(property.Id, null, "Today", null, null, today, null, null, null));
        await CreateTaskAsync(client, new MaintenanceTaskInput(property.Id, null, "Soon", null, null, today.AddDays(30), null, null, null));
        await CreateTaskAsync(client, new MaintenanceTaskInput(property.Id, null, "Later", null, null, today.AddDays(31), null, null, null));
        await CreateTaskAsync(client, new MaintenanceTaskInput(property.Id, null, "Unscheduled", null, null, null, null, null, null));

        var dashboard = await client.GetFromJsonAsync<DashboardDto>("/api/dashboard");

        Assert.Equal(1, dashboard!.MaintenanceOverdue);
        Assert.Equal(2, dashboard.MaintenanceDueSoon);
    }

    [Fact]
    public async Task MovingFixtureOrRoom_KeepsMaintenanceAndAssetsInTheRightProperty()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = CreateClient(factory);
        var home = await CreatePropertyAsync(client, "Home");
        var cottage = await CreatePropertyAsync(client, "Cottage");
        var homeRoom = await CreateRoomAsync(client, (await CreateFloorAsync(client, home.Id)).Id);
        var cottageFloor = await CreateFloorAsync(client, cottage.Id);
        var cottageRoom = await CreateRoomAsync(client, cottageFloor.Id);
        var fixture = await (await client.PostAsJsonAsync($"/api/rooms/{homeRoom.Id}/fixtures", FixtureFor(homeRoom.Id))).Content.ReadFromJsonAsync<FixtureDto>();
        var task = await CreateTaskAsync(client, new MaintenanceTaskInput(home.Id, fixture!.Id, "Service", null, null, null, null, null, null));

        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/fixtures/{fixture.Id}", FixtureFor(cottageRoom.Id))).StatusCode);
        Assert.Equal(cottage.Id, Assert.Single((await client.GetFromJsonAsync<List<MaintenanceTaskDto>>("/api/maintenance"))!).PropertyId);

        await CreateAssetAsync(client, home.Id, homeRoom.Id, null, "Couch");
        var moveRoom = await client.PutAsJsonAsync($"/api/rooms/{homeRoom.Id}", new RoomInput(cottageFloor.Id, "Lounge", null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null));
        Assert.Equal(HttpStatusCode.BadRequest, moveRoom.StatusCode);
        Assert.Equal(task.Id, Assert.Single((await client.GetFromJsonAsync<List<MaintenanceTaskDto>>($"/api/maintenance?propertyId={cottage.Id}"))!).Id);
    }

    [Fact]
    public async Task Maintenance_RoundTripsThroughBackupIdempotently()
    {
        InventoryExport backup;
        using (var sourceFactory = new CustomWebApplicationFactory())
        using (var source = CreateClient(sourceFactory))
        {
            var property = await CreatePropertyAsync(source);
            var room = await CreateRoomAsync(source, (await CreateFloorAsync(source, property.Id)).Id);
            var fixture = await (await source.PostAsJsonAsync($"/api/rooms/{room.Id}/fixtures", FixtureFor(room.Id))).Content.ReadFromJsonAsync<FixtureDto>();
            var task = await CreateTaskAsync(source, new MaintenanceTaskInput(property.Id, fixture!.Id, "Service aircon", 1, "years", null, null, null, null));
            await source.PostAsJsonAsync($"/api/maintenance/{task.Id}/complete", new MaintenanceCompletionInput(new DateOnly(2026, 3, 1), 900m, "CoolAir", null));
            backup = (await source.GetFromJsonAsync<InventoryExport>("/api/export"))!;
        }
        Assert.Single(backup.MaintenanceTasks!);
        Assert.Single(backup.MaintenanceRecords!);

        using var targetFactory = new CustomWebApplicationFactory();
        using var target = CreateClient(targetFactory);
        var preview = (await (await target.PostAsJsonAsync("/api/import/preview", backup)).Content.ReadFromJsonAsync<ImportPreviewDto>())!;
        Assert.True(preview.IsValid, string.Join("; ", preview.Errors));
        Assert.Equal(1, preview.MaintenanceTasks);
        for (var attempt = 0; attempt < 2; attempt++)
            Assert.Equal(HttpStatusCode.NoContent, (await target.PostAsJsonAsync("/api/import/confirm", new { inventory = backup, skipExternalIds = new List<string>() })).StatusCode);

        var task2 = Assert.Single((await target.GetFromJsonAsync<List<MaintenanceTaskDto>>("/api/maintenance"))!);
        Assert.Equal(new DateOnly(2027, 3, 1), task2.DueOn);
        Assert.Equal("Sink", task2.FixtureName);
        Assert.Equal(900m, Assert.Single((await target.GetFromJsonAsync<List<MaintenanceRecordDto>>($"/api/maintenance/{task2.Id}/history"))!).Cost);
    }

    static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52, 1, 2, 3];
    static readonly byte[] PdfBytes = "%PDF-1.7\n% test document\n"u8.ToArray();

    [Fact]
    public async Task Upload_SniffsTypeEnforcesLimitAndServesFile()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = CreateClient(factory);

        var uploaded = await UploadAsync(client, PngBytes, "kitchen.jpg"); // misleading name: the stored type comes from the bytes
        Assert.Matches(@"^\d{4}/\d{2}/[0-9a-f]{32}\.png$", uploaded.StorageKey);
        Assert.Equal("image/png", uploaded.ContentType);
        Assert.Equal("kitchen.jpg", uploaded.FileName);

        var download = await client.GetAsync($"/api/files/{uploaded.StorageKey}");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("image/png", download.Content.Headers.ContentType?.MediaType);
        Assert.Equal("nosniff", download.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal(PngBytes, await download.Content.ReadAsByteArrayAsync());

        Assert.Equal(HttpStatusCode.BadRequest, (await PostFileAsync(client, "<script>alert(1)</script>"u8.ToArray(), "photo.png")).StatusCode);
        var tooBig = new byte[FileStore.MaxBytes + 1];
        PdfBytes.CopyTo(tooBig, 0);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostFileAsync(client, tooBig, "huge.pdf")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/files/..%2F..%2Finventory.db")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/files/2026/01/{Guid.NewGuid():N}.png")).StatusCode);
    }

    [Fact]
    public async Task Documents_AttachValidateSearchAndDriveDashboard()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = CreateClient(factory);
        var home = await CreatePropertyAsync(client, "Home");
        var cottage = await CreatePropertyAsync(client, "Cottage");
        var tv = await CreateAssetAsync(client, home.Id, null, null, "TV");
        await CreateAssetAsync(client, home.Id, null, null, "Sofa");
        var cottageAsset = await CreateAssetAsync(client, cottage.Id, null, null, "Kettle");
        var file = await UploadAsync(client, PdfBytes, "receipt.pdf");
        var today = DateOnly.FromDateTime(DateTime.Today);
        DocumentInput Doc(Guid propertyId, string title, string kind, Guid? assetId = null, DateOnly? expires = null, Guid? roomId = null) =>
            new(propertyId, roomId, null, assetId, null, title, kind, file.StorageKey, file.FileName, file.ContentType, file.SizeBytes, today, expires, "electronics, samsung", null);

        var receipt = await client.PostAsJsonAsync("/api/documents", Doc(home.Id, "TV receipt", "Receipt", tv.Id));
        Assert.Equal(HttpStatusCode.Created, receipt.StatusCode);
        Assert.Equal("Asset: TV", (await receipt.Content.ReadFromJsonAsync<DocumentDto>())!.AttachedTo);
        await client.PostAsJsonAsync("/api/documents", Doc(home.Id, "TV warranty", "Warranty", tv.Id, today.AddDays(60)));
        await client.PostAsJsonAsync("/api/documents", Doc(home.Id, "Old warranty", "Warranty", tv.Id, today.AddDays(-1)));

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/documents", Doc(home.Id, "Wrong property", "Receipt", cottageAsset.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/documents", Doc(home.Id, "Bad kind", "Selfie"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/documents", Doc(home.Id, "Two targets", "Receipt", tv.Id, roomId: Guid.NewGuid()))).StatusCode);

        var dashboard = await client.GetFromJsonAsync<DashboardDto>("/api/dashboard");
        Assert.Equal(1, dashboard!.WarrantiesExpiring);
        Assert.Equal(2, dashboard.AssetsMissingReceipts); // Sofa and the cottage kettle

        Assert.Equal(3, (await client.GetFromJsonAsync<List<DocumentDto>>($"/api/documents?assetId={tv.Id}"))!.Count);
        Assert.Equal(2, (await client.GetFromJsonAsync<List<DocumentDto>>("/api/documents?kind=Warranty"))!.Count);
        var results = await client.GetFromJsonAsync<List<SearchResultDto>>("/api/search?q=samsung");
        Assert.Equal(3, results!.Count(x => x.Kind == "Document"));
    }

    [Fact]
    public async Task DeletingPhotosDocumentsAndRooms_RemovesFilesNoLongerUsed()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = CreateClient(factory);
        var property = await CreatePropertyAsync(client);
        var room = await CreateRoomAsync(client, (await CreateFloorAsync(client, property.Id)).Id);
        var shared = await UploadAsync(client, PngBytes, "shared.png");
        var roomOnly = await UploadAsync(client, PngBytes, "room.png");
        bool Exists(UploadedFileDto f) => File.Exists(Path.Combine(factory.FilesPath, f.StorageKey));

        var photo = await (await client.PostAsJsonAsync($"/api/properties/{property.Id}/photos", new PhotoMetadataInput(shared.StorageKey, null, 0))).Content.ReadFromJsonAsync<PhotoMetadataDto>();
        var document = await (await client.PostAsJsonAsync("/api/documents", new DocumentInput(property.Id, null, null, null, null, "Front", "Photo", shared.StorageKey, null, null, null, null, null, null, null))).Content.ReadFromJsonAsync<DocumentDto>();
        await client.PostAsJsonAsync($"/api/rooms/{room.Id}/photos", new PhotoMetadataInput(roomOnly.StorageKey, null, 0));

        await client.DeleteAsync($"/api/properties/{property.Id}/photos/{photo!.Id}");
        Assert.True(Exists(shared)); // still used by the document
        await client.DeleteAsync($"/api/documents/{document!.Id}");
        Assert.False(Exists(shared));

        Assert.True(Exists(roomOnly));
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/rooms/{room.Id}")).StatusCode);
        Assert.False(Exists(roomOnly));
    }

    [Fact]
    public async Task Documents_RoundTripThroughBackupAndSurviveRoomDelete()
    {
        InventoryExport backup;
        using (var sourceFactory = new CustomWebApplicationFactory())
        using (var source = CreateClient(sourceFactory))
        {
            var property = await CreatePropertyAsync(source);
            var room = await CreateRoomAsync(source, (await CreateFloorAsync(source, property.Id)).Id);
            var asset = await CreateAssetAsync(source, property.Id, null, null, "TV");
            var file = await UploadAsync(source, PdfBytes, "manual.pdf");
            await source.PostAsJsonAsync("/api/documents", new DocumentInput(property.Id, null, null, asset.Id, null, "TV manual", "Manual", file.StorageKey, file.FileName, file.ContentType, file.SizeBytes, null, null, null, null));
            var plan = await (await source.PostAsJsonAsync("/api/documents", new DocumentInput(property.Id, room.Id, null, null, null, "Lounge plan", "Plan", file.StorageKey, null, null, null, null, null, null, null))).Content.ReadFromJsonAsync<DocumentDto>();

            // Deleting the room keeps its document at property level instead of losing it.
            await source.DeleteAsync($"/api/rooms/{room.Id}");
            var kept = Assert.Single((await source.GetFromJsonAsync<List<DocumentDto>>("/api/documents"))!, x => x.Id == plan!.Id);
            Assert.Null(kept.RoomId);
            backup = (await source.GetFromJsonAsync<InventoryExport>("/api/export"))!;
        }
        Assert.Equal(2, backup.Documents!.Count);

        using var targetFactory = new CustomWebApplicationFactory();
        using var target = CreateClient(targetFactory);
        var preview = (await (await target.PostAsJsonAsync("/api/import/preview", backup)).Content.ReadFromJsonAsync<ImportPreviewDto>())!;
        Assert.True(preview.IsValid, string.Join("; ", preview.Errors));
        Assert.Equal(2, preview.Documents);
        for (var attempt = 0; attempt < 2; attempt++)
            Assert.Equal(HttpStatusCode.NoContent, (await target.PostAsJsonAsync("/api/import/confirm", new { inventory = backup, skipExternalIds = new List<string>() })).StatusCode);
        var documents = (await target.GetFromJsonAsync<List<DocumentDto>>("/api/documents"))!;
        Assert.Equal(2, documents.Count);
        Assert.Equal("Asset: TV", Assert.Single(documents, x => x.Kind == "Manual").AttachedTo);
    }

    [Fact]
    public async Task ZipBackup_RestoresFilesAndRecordsIntoFreshInventory()
    {
        byte[] zipBytes;
        UploadedFileDto photo, manual;
        using (var sourceFactory = new CustomWebApplicationFactory())
        using (var source = CreateClient(sourceFactory))
        {
            var property = await CreatePropertyAsync(source);
            photo = await UploadAsync(source, PngBytes, "front.png");
            manual = await UploadAsync(source, PdfBytes, "manual.pdf");
            await source.PostAsJsonAsync($"/api/properties/{property.Id}/photos", new PhotoMetadataInput(photo.StorageKey, "Front", 0));
            await source.PostAsJsonAsync($"/api/properties/{property.Id}/photos", new PhotoMetadataInput("C:/elsewhere/old.jpg", "External", 1));
            await source.PostAsJsonAsync("/api/documents", new DocumentInput(property.Id, null, null, null, null, "Manual", "Manual", manual.StorageKey, null, null, null, null, null, null, null));
            var response = await source.GetAsync("/api/backup");
            Assert.Equal("application/zip", response.Content.Headers.ContentType?.MediaType);
            zipBytes = await response.Content.ReadAsByteArrayAsync();
        }
        using (var zip = new System.IO.Compression.ZipArchive(new MemoryStream(zipBytes)))
            Assert.Equal(new[] { "files/" + manual.StorageKey, "files/" + photo.StorageKey, "inventory.json" }.Order(), zip.Entries.Select(x => x.FullName).Order());

        using var targetFactory = new CustomWebApplicationFactory();
        using var target = CreateClient(targetFactory);
        var restore = await target.PostAsync("/api/import/zip", new ByteArrayContent(zipBytes));
        Assert.Equal(HttpStatusCode.OK, restore.StatusCode);
        var result = (await restore.Content.ReadFromJsonAsync<ZipImportDto>())!;
        Assert.Equal(2, result.FilesRestored);
        Assert.Equal(HttpStatusCode.NoContent, (await target.PostAsJsonAsync("/api/import/confirm", new { inventory = result.Inventory, skipExternalIds = new List<string>() })).StatusCode);

        Assert.Equal(PngBytes, await target.GetByteArrayAsync($"/api/files/{photo.StorageKey}"));
        Assert.Equal(PdfBytes, await target.GetByteArrayAsync($"/api/files/{manual.StorageKey}"));
        Assert.Equal(2, (await target.GetFromJsonAsync<List<PhotoMetadataDto>>($"/api/properties/{Assert.Single((await target.GetFromJsonAsync<List<PropertyDto>>("/api/properties"))!).Id}/photos"))!.Count);

        // Restoring the same ZIP again skips files that already exist.
        var again = (await (await target.PostAsync("/api/import/zip", new ByteArrayContent(zipBytes))).Content.ReadFromJsonAsync<ZipImportDto>())!;
        Assert.Equal((0, 2), (again.FilesRestored, again.FilesSkipped));
    }

    [Fact]
    public async Task ZipRestore_IgnoresUnsafeOrDisguisedEntries()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = CreateClient(factory);
        var goodKey = $"2026/01/{Guid.NewGuid():N}.png";
        var disguisedKey = $"2026/01/{Guid.NewGuid():N}.png";
        var stream = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string name, byte[] bytes) { using var entry = zip.CreateEntry(name).Open(); entry.Write(bytes); }
            Add("inventory.json", System.Text.Encoding.UTF8.GetBytes("""{"schemaVersion":1,"properties":[],"floors":[],"rooms":[],"surfaces":[],"storageLocations":[],"assets":[],"propertyPhotos":[]}"""));
            Add("files/" + goodKey, PngBytes);
            Add("files/" + disguisedKey, "<html>not an image</html>"u8.ToArray());
            Add("files/../../escape.png", PngBytes);
            Add("files/2026/01/notakey.png", PngBytes);
        }

        var result = (await (await client.PostAsync("/api/import/zip", new ByteArrayContent(stream.ToArray()))).Content.ReadFromJsonAsync<ZipImportDto>())!;

        Assert.Equal((1, 3), (result.FilesRestored, result.FilesSkipped));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/files/{goodKey}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/files/{disguisedKey}")).StatusCode);
        Assert.False(File.Exists(Path.Combine(factory.FilesPath, "..", "escape.png")));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/import/zip", new ByteArrayContent("not a zip"u8.ToArray()))).StatusCode);
    }

    [Fact]
    public async Task Utilities_AreFixturesWithCategoryProviderAndAccount()
    {
        InventoryExport backup;
        using (var sourceFactory = new CustomWebApplicationFactory())
        using (var source = CreateClient(sourceFactory))
        {
            var property = await CreatePropertyAsync(source);
            var room = await CreateRoomAsync(source, (await CreateFloorAsync(source, property.Id)).Id);
            await source.PostAsJsonAsync($"/api/rooms/{room.Id}/fixtures", FixtureFor(room.Id));
            var meter = await (await source.PostAsJsonAsync($"/api/rooms/{room.Id}/fixtures", FixtureFor(room.Id) with { Name = "Electricity meter", Type = "Electricity meter", Category = "utility", Provider = " City Power ", AccountNumber = "ACC-42" })).Content.ReadFromJsonAsync<FixtureDto>();
            Assert.Equal(("Utility", "City Power", "ACC-42"), (meter!.Category, meter.Provider, meter.AccountNumber));

            var utilities = await source.GetFromJsonAsync<List<FixtureDto>>("/api/fixtures?category=Utility");
            Assert.Equal("Electricity meter", Assert.Single(utilities!).Name);
            Assert.Equal("Sink", Assert.Single((await source.GetFromJsonAsync<List<FixtureDto>>("/api/fixtures?category=Fixture"))!).Name);

            // Utilities get maintenance like any fixture.
            await CreateTaskAsync(source, new MaintenanceTaskInput(property.Id, meter.Id, "Read meter", 1, "months", null, null, null, null));
            backup = (await source.GetFromJsonAsync<InventoryExport>("/api/export"))!;
        }

        using var targetFactory = new CustomWebApplicationFactory();
        using var target = CreateClient(targetFactory);
        Assert.Equal(HttpStatusCode.NoContent, (await target.PostAsJsonAsync("/api/import/confirm", new { inventory = backup, skipExternalIds = new List<string>() })).StatusCode);
        var restored = Assert.Single((await target.GetFromJsonAsync<List<FixtureDto>>("/api/fixtures?category=utility"))!);
        Assert.Equal(("Utility", "City Power", "ACC-42"), (restored.Category, restored.Provider, restored.AccountNumber));
    }

    [Fact]
    public async Task AssetHistory_RecordsKeyEventsAndManualEntries()
    {
        InventoryExport backup;
        using (var sourceFactory = new CustomWebApplicationFactory())
        using (var source = CreateClient(sourceFactory))
        {
            var property = await CreatePropertyAsync(source);
            var lounge = await CreateRoomAsync(source, (await CreateFloorAsync(source, property.Id)).Id, "Lounge");
            var garage = await CreateLocationAsync(source, property.Id, null, "Garage");
            var tv = await (await source.PostAsJsonAsync("/api/assets", new AssetInput(property.Id, lounge.Id, null, "TV", "Electronics", null, null, null, null, new DateOnly(2024, 5, 1), 9000m, null, null, null))).Content.ReadFromJsonAsync<AssetDto>();

            await source.PostAsJsonAsync($"/api/assets/{tv!.Id}/move", new AssetMoveInput(null, garage.Id));
            await source.PostAsJsonAsync($"/api/assets/{tv.Id}/move", new AssetMoveInput(null, garage.Id)); // no change, no event
            var repair = await source.PostAsJsonAsync($"/api/assets/{tv.Id}/events", new AssetEventInput(new DateOnly(2025, 2, 3), "Repaired", "New backlight", 1200m));
            Assert.Equal(HttpStatusCode.Created, repair.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await source.PostAsJsonAsync($"/api/assets/{tv.Id}/events", new AssetEventInput(new DateOnly(2025, 2, 3), "Moved", null, null))).StatusCode);
            await source.PostAsync($"/api/assets/{tv.Id}/archive", null);
            await source.PostAsync($"/api/assets/{tv.Id}/unarchive", null);

            var history = (await source.GetFromJsonAsync<List<AssetEventDto>>($"/api/assets/{tv.Id}/history"))!;
            Assert.Equal(["Added", "Archived", "Moved", "Repaired", "Unarchived"], history.Select(x => x.Kind).Order());
            var added = Assert.Single(history, x => x.Kind == "Added");
            Assert.Equal((new DateOnly(2024, 5, 1), 9000m), (added.OccurredOn, added.Cost));
            Assert.Equal("Main House · Ground Floor · Lounge → Garage", Assert.Single(history, x => x.Kind == "Moved").Description);

            // Automatic events can't be removed; manual ones can.
            Assert.Equal(HttpStatusCode.BadRequest, (await source.DeleteAsync($"/api/assets/{tv.Id}/events/{added.Id}")).StatusCode);
            backup = (await source.GetFromJsonAsync<InventoryExport>("/api/export"))!;
        }
        Assert.Equal(5, backup.AssetEvents!.Count);

        using var targetFactory = new CustomWebApplicationFactory();
        using var target = CreateClient(targetFactory);
        for (var attempt = 0; attempt < 2; attempt++)
            Assert.Equal(HttpStatusCode.NoContent, (await target.PostAsJsonAsync("/api/import/confirm", new { inventory = backup, skipExternalIds = new List<string>() })).StatusCode);
        var restoredTv = Assert.Single((await target.GetFromJsonAsync<List<AssetDto>>("/api/assets?archived=false"))!);
        var restored = (await target.GetFromJsonAsync<List<AssetEventDto>>($"/api/assets/{restoredTv.Id}/history"))!;
        Assert.Equal(5, restored.Count);
        Assert.Equal(1200m, Assert.Single(restored, x => x.Kind == "Repaired").Cost);
    }

    [Fact]
    public async Task InsuranceReport_ValuesActiveItemsPerPropertyInItsCurrency()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = CreateClient(factory);
        var home = (await (await client.PostAsJsonAsync("/api/properties", new PropertyInput("Home", "1 Main Rd", null, null, null, null, "ZAR"))).Content.ReadFromJsonAsync<PropertyDto>())!;
        var flat = (await (await client.PostAsJsonAsync("/api/properties", new PropertyInput("Flat", null, null, null, null, null, "EUR"))).Content.ReadFromJsonAsync<PropertyDto>())!;
        var room = await CreateRoomAsync(client, (await CreateFloorAsync(client, home.Id)).Id);
        var tv = (await (await client.PostAsJsonAsync("/api/assets", new AssetInput(home.Id, room.Id, null, "TV", "Electronics", null, "Samsung", "QE55", "SN1", new DateOnly(2024, 1, 1), 15000m, 12000m, null, null))).Content.ReadFromJsonAsync<AssetDto>())!;
        var oldTv = await CreateAssetAsync(client, home.Id, null, null, "Old TV");
        await client.PostAsync($"/api/assets/{oldTv.Id}/archive", null);
        await client.PostAsJsonAsync($"/api/rooms/{room.Id}/fixtures", FixtureFor(room.Id) with { CurrentValue = 3000m });
        await client.PostAsJsonAsync("/api/assets", new AssetInput(flat.Id, null, null, "Sofa", "Furniture", null, null, null, null, null, null, 800m, null, null));
        var receipt = await UploadAsync(client, PdfBytes, "tv.pdf");
        await client.PostAsJsonAsync("/api/documents", new DocumentInput(home.Id, null, null, tv.Id, null, "TV receipt", "Receipt", receipt.StorageKey, null, null, null, null, null, null, null));
        await client.PostAsJsonAsync($"/api/assets/{tv.Id}/photos", new AssetPhotoInput("tv.jpg", null, 0));

        var report = (await client.GetFromJsonAsync<InsuranceReportDto>("/api/reports/insurance"))!;

        Assert.Equal(["Flat", "Home"], report.Properties.Select(x => x.Name));
        var homeReport = report.Properties.Single(x => x.Name == "Home");
        Assert.Equal(("ZAR", 12000m, 3000m), (homeReport.Currency, homeReport.AssetTotal, homeReport.FixtureTotal));
        Assert.Equal(2, homeReport.Items.Count); // archived Old TV is excluded
        var tvItem = Assert.Single(homeReport.Items, x => x.Kind == "Asset");
        Assert.Equal(("Samsung QE55", "SN1", 1, true), (tvItem.BrandModel, tvItem.SerialNumber, tvItem.PhotoCount, tvItem.HasReceipt));
        Assert.False(Assert.Single(homeReport.Items, x => x.Kind == "Fixture").HasReceipt);
        Assert.Equal(("EUR", 800m), (report.Properties.Single(x => x.Name == "Flat").Currency, report.Properties.Single(x => x.Name == "Flat").AssetTotal));

        Assert.Equal("Flat", Assert.Single((await client.GetFromJsonAsync<InsuranceReportDto>($"/api/reports/insurance?propertyId={flat.Id}"))!.Properties).Name);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/reports/insurance?propertyId={Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task QrCodes_AndSingleAssetLookupSupportLabels()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = CreateClient(factory);
        var property = await CreatePropertyAsync(client);
        var asset = await CreateAssetAsync(client, property.Id, null, null, "Drill");

        var qr = await client.GetAsync($"/api/qr?text={Uri.EscapeDataString($"https://localhost/scan/asset/{asset.Id}")}");
        Assert.Equal(HttpStatusCode.OK, qr.StatusCode);
        Assert.Equal("image/svg+xml", qr.Content.Headers.ContentType?.MediaType);
        Assert.Contains("<svg", await qr.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/qr")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/qr?text={new string('x', 513)}")).StatusCode);

        await client.PostAsync($"/api/assets/{asset.Id}/archive", null);
        var found = await client.GetFromJsonAsync<AssetDto>($"/api/assets/{asset.Id}");
        Assert.True(found!.IsArchived); // labels on archived items still resolve
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/assets/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Search_IsCaseInsensitiveAndTreatsWildcardsLiterally()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = CreateClient(factory);
        var property = await CreatePropertyAsync(client);
        await CreateAssetAsync(client, property.Id, null, null, "Cordless DRILL");
        await CreateAssetAsync(client, property.Id, null, null, "100% wool rug");
        await CreateAssetAsync(client, property.Id, null, null, "1000 wool rug");
        var archived = await CreateAssetAsync(client, property.Id, null, null, "Old drill");
        await client.PostAsync($"/api/assets/{archived.Id}/archive", null);

        var drills = await client.GetFromJsonAsync<List<SearchResultDto>>("/api/search?q=drill");
        Assert.Equal("Cordless DRILL", Assert.Single(drills!).Title); // archived assets are not searched
        var percent = await client.GetFromJsonAsync<List<SearchResultDto>>($"/api/search?q={Uri.EscapeDataString("100%")}");
        Assert.Equal("100% wool rug", Assert.Single(percent!).Title);
        Assert.Empty((await client.GetFromJsonAsync<List<SearchResultDto>>("/api/search?q=%20"))!);
    }

    [Fact]
    public async Task Paints_CrudAndAssignmentRules()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = CreateClient(factory);
        var property = await CreatePropertyAsync(client);
        var room = await CreateRoomAsync(client, (await CreateFloorAsync(client, property.Id)).Id);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/paints", new PaintInput(" ", "White", null, null, null))).StatusCode);
        var paint = (await (await client.PostAsJsonAsync("/api/paints", new PaintInput(" Dulux ", "Oyster White", "OW-104", null, null))).Content.ReadFromJsonAsync<PaintDto>())!;
        Assert.Equal("Dulux", paint.Brand);
        var updated = (await (await client.PutAsJsonAsync($"/api/paints/{paint.Id}", new PaintInput("Dulux", "Oyster White", "OW-104", "Matt", "Lounge colour"))).Content.ReadFromJsonAsync<PaintDto>())!;
        Assert.Equal(("Matt", "Lounge colour"), (updated.Finish, updated.Notes));

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/rooms/{room.Id}/paints", new RoomPaintInput(paint.Id, 0, "Walls"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/rooms/{room.Id}/paints", new RoomPaintInput(paint.Id, 1, "Again"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/rooms/{room.Id}/paints", new RoomPaintInput(Guid.NewGuid(), 0, null))).StatusCode);
        var assignments = (await (await client.PutAsJsonAsync($"/api/rooms/{room.Id}/paints/{paint.Id}", new RoomPaintInput(paint.Id, 2, "Feature wall"))).Content.ReadFromJsonAsync<List<RoomPaintDto>>())!;
        Assert.Equal("Feature wall", Assert.Single(assignments).Surface);

        // Deleting a paint removes its room assignments.
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/paints/{paint.Id}")).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<List<RoomPaintDto>>($"/api/rooms/{room.Id}/paints"))!);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/paints/{paint.Id}")).StatusCode);
    }

    [Fact]
    public async Task PropertyAndRoomPhotos_UpdateAndDelete()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = CreateClient(factory);
        var property = await CreatePropertyAsync(client);
        var room = await CreateRoomAsync(client, (await CreateFloorAsync(client, property.Id)).Id);

        foreach (var endpoint in new[] { $"/api/properties/{property.Id}/photos", $"/api/rooms/{room.Id}/photos" })
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(endpoint, new PhotoMetadataInput(" ", null, 0))).StatusCode);
            var photo = (await (await client.PostAsJsonAsync(endpoint, new PhotoMetadataInput("a.jpg", "Before", 1))).Content.ReadFromJsonAsync<PhotoMetadataDto>())!;
            var updated = (await (await client.PutAsJsonAsync($"{endpoint}/{photo.Id}", new PhotoMetadataInput("b.jpg", "After", 2))).Content.ReadFromJsonAsync<PhotoMetadataDto>())!;
            Assert.Equal(("b.jpg", "After", 2), (updated.StorageKey, updated.Caption, updated.SortOrder));
            Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"{endpoint}/{photo.Id}")).StatusCode);
            Assert.Empty((await client.GetFromJsonAsync<List<PhotoMetadataDto>>(endpoint))!);
            Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"{endpoint}/{photo.Id}")).StatusCode);
        }
    }

    [Fact]
    public async Task AssetBarcodeAndManual_SaveSearchAndRoundTrip()
    {
        InventoryExport backup;
        using (var sourceFactory = new CustomWebApplicationFactory())
        using (var source = CreateClient(sourceFactory))
        {
            var property = await CreatePropertyAsync(source);
            var created = await source.PostAsJsonAsync("/api/assets", new AssetInput(property.Id, null, null, "Kettle", "Kitchen", null, null, null, null, null, null, null, null, null, " 6001234567890 ", "https://example.com/kettle.pdf"));
            var kettle = (await created.Content.ReadFromJsonAsync<AssetDto>())!;
            Assert.Equal(("6001234567890", "https://example.com/kettle.pdf"), (kettle.Barcode, kettle.ManualUrl));
            Assert.Equal("Kettle", Assert.Single((await source.GetFromJsonAsync<List<SearchResultDto>>("/api/search?q=600123"))!).Title);
            backup = (await source.GetFromJsonAsync<InventoryExport>("/api/export"))!;
        }

        using var targetFactory = new CustomWebApplicationFactory();
        using var target = CreateClient(targetFactory);
        await target.PostAsJsonAsync("/api/import/confirm", new { inventory = backup, skipExternalIds = new List<string>() });
        var restored = Assert.Single((await target.GetFromJsonAsync<List<AssetDto>>("/api/assets?archived=false"))!);
        Assert.Equal(("6001234567890", "https://example.com/kettle.pdf"), (restored.Barcode, restored.ManualUrl));
    }

    [Fact]
    public async Task Import_ConvertsLegacyFixtureMaintenanceTextIntoTasks()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = CreateClient(factory);
        // An older backup: fixtures still carried free-text maintenance fields and there were no maintenance tasks.
        var legacy = new InventoryExport(1,
            [new ImportProperty("p", "Home", null, null, null, null, null)],
            [new ImportFloor("f", "p", "Ground", null)],
            [new ImportRoom("r", "f", "Kitchen", null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null)],
            [], [], [], [],
            [new ImportFixture("geyser", "r", "Geyser", "Geyser", null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, "Every 2 years", new DateOnly(2025, 3, 1)),
             new ImportFixture("sink", "r", "Sink", "Sink", null, null, null, null, null, null, null, null, null, null, null, null)]);

        for (var attempt = 0; attempt < 2; attempt++)
            Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/import/confirm", new { inventory = legacy, skipExternalIds = new List<string>() })).StatusCode);

        var task = Assert.Single((await client.GetFromJsonAsync<List<MaintenanceTaskDto>>("/api/maintenance"))!);
        Assert.Equal(("Service: Geyser", "Every 2 years", new DateOnly(2025, 3, 1), "Geyser"), (task.Title, task.Notes, task.LastCompletedOn, task.FixtureName));
    }

    [Fact]
    public async Task Dashboard_ShowsRecentPurchasesAndRoomCompletion()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = CreateClient(factory);
        var property = await CreatePropertyAsync(client);
        var floor = await CreateFloorAsync(client, property.Id);
        var today = DateOnly.FromDateTime(DateTime.Today);
        await client.PostAsJsonAsync("/api/assets", new AssetInput(property.Id, null, null, "New TV", "Electronics", null, null, null, null, today.AddDays(-3), 9000m, null, null, null));
        await client.PostAsJsonAsync("/api/assets", new AssetInput(property.Id, null, null, "Old sofa", "Furniture", null, null, null, null, today.AddYears(-2), 5000m, null, null, null));

        var complete = (await (await client.PostAsJsonAsync("/api/rooms", new RoomInput(floor.Id, "Kitchen", null, 12m, null, null, null, null, null, "Tile", "Paint", null, null, null, null, null, null, null))).Content.ReadFromJsonAsync<RoomDto>())!;
        await client.PostAsJsonAsync($"/api/rooms/{complete.Id}/surfaces", SurfaceFor(complete.Id, "Wall", "wall"));
        await client.PostAsJsonAsync($"/api/rooms/{complete.Id}/photos", new PhotoMetadataInput("kitchen.jpg", null, 0));
        await CreateRoomAsync(client, floor.Id, "Empty room");

        var dashboard = (await client.GetFromJsonAsync<DashboardDto>("/api/dashboard"))!;
        Assert.Equal("New TV", Assert.Single(dashboard.RecentPurchases!).Name);
        Assert.Equal((1, 2, 50), (dashboard.RoomsComplete, dashboard.RoomCount, dashboard.RoomCompletionPercent));

        var completion = (await client.GetFromJsonAsync<List<RoomCompletionDto>>("/api/rooms/completion"))!;
        Assert.Equal(["dimensions", "flooring", "wall finish", "surfaces", "photos"], completion.First(x => x.RoomId != complete.Id).Missing);
        Assert.Empty(completion.Single(x => x.RoomId == complete.Id).Missing);
    }

    [Fact]
    public async Task ChangingAssetValue_AddsValuedHistoryEntry()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = CreateClient(factory);
        var property = await CreatePropertyAsync(client);
        var asset = (await (await client.PostAsJsonAsync("/api/assets", new AssetInput(property.Id, null, null, "Ring", "Jewellery", null, null, null, null, null, null, 1000m, null, null))).Content.ReadFromJsonAsync<AssetDto>())!;

        await client.PutAsJsonAsync($"/api/assets/{asset.Id}", new AssetInput(property.Id, null, null, "Ring", "Jewellery", null, null, null, null, null, null, 1500m, null, "Revalued"));
        await client.PutAsJsonAsync($"/api/assets/{asset.Id}", new AssetInput(property.Id, null, null, "Ring", "Jewellery", null, null, null, null, null, null, 1500m, null, "Notes only"));

        var history = (await client.GetFromJsonAsync<List<AssetEventDto>>($"/api/assets/{asset.Id}/history"))!;
        Assert.Equal("Value 1000.00 → 1500.00", Assert.Single(history, x => x.Kind == "Valued").Description);
    }

    [Fact]
    public async Task Contacts_CrudValidationLookupsSearchAndBackup()
    {
        InventoryExport backup;
        using (var sourceFactory = new CustomWebApplicationFactory())
        using (var source = CreateClient(sourceFactory))
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await source.PostAsJsonAsync("/api/contacts", new ContactInput("Bob", null, "Wizard", null, null, null, null))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await source.PostAsJsonAsync("/api/contacts", new ContactInput("Bob", null, "Installer", null, "not-an-email", null, null))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await source.PostAsJsonAsync("/api/contacts", new ContactInput("Bob", null, "Installer", null, null, "javascript:alert(1)", null))).StatusCode);
            var bob = (await (await source.PostAsJsonAsync("/api/contacts", new ContactInput(" Bob Builder ", "CoolAir", "Installer", "021 555 0101", "bob@coolair.example", "https://coolair.example", "Aircon specialist"))).Content.ReadFromJsonAsync<ContactDto>())!;
            Assert.Equal("Bob Builder", bob.Name);
            var updated = (await (await source.PutAsJsonAsync($"/api/contacts/{bob.Id}", new ContactInput("Bob Builder", "CoolAir", "Service provider", "021 555 0101", null, null, null))).Content.ReadFromJsonAsync<ContactDto>())!;
            Assert.Equal("Service provider", updated.Kind);

            var property = await CreatePropertyAsync(source);
            await source.PostAsJsonAsync("/api/assets", new AssetInput(property.Id, null, null, "TV", "Electronics", null, "Samsung", null, null, null, null, null, null, null));
            var lookups = (await source.GetFromJsonAsync<LookupsDto>("/api/lookups"))!;
            Assert.Equal(["Electronics"], lookups.AssetCategories);
            Assert.Equal(["Samsung"], lookups.Brands);
            Assert.Equal(["Bob Builder", "CoolAir"], lookups.ContactNames);
            Assert.Equal("Bob Builder", Assert.Single((await source.GetFromJsonAsync<List<SearchResultDto>>("/api/search?q=coolair"))!, x => x.Kind == "Contact").Title);
            backup = (await source.GetFromJsonAsync<InventoryExport>("/api/export"))!;
        }

        using var targetFactory = new CustomWebApplicationFactory();
        using var target = CreateClient(targetFactory);
        for (var attempt = 0; attempt < 2; attempt++)
            await target.PostAsJsonAsync("/api/import/confirm", new { inventory = backup, skipExternalIds = new List<string>() });
        var restored = Assert.Single((await target.GetFromJsonAsync<List<ContactDto>>("/api/contacts"))!);
        Assert.Equal(HttpStatusCode.NoContent, (await target.DeleteAsync($"/api/contacts/{restored.Id}")).StatusCode);
    }

    [Fact]
    public async Task OrphanSweep_RemovesOnlyOldUnreferencedFiles()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = CreateClient(factory);
        var property = await CreatePropertyAsync(client);
        var kept = await UploadAsync(client, PngBytes, "kept.png");
        var orphan = await UploadAsync(client, PngBytes, "orphan.png");
        var fresh = await UploadAsync(client, PngBytes, "fresh.png");
        await client.PostAsJsonAsync($"/api/properties/{property.Id}/photos", new PhotoMetadataInput(kept.StorageKey, null, 0));
        string PathOf(UploadedFileDto f) => Path.Combine(factory.FilesPath, f.StorageKey);
        foreach (var file in new[] { kept, orphan }) File.SetLastWriteTimeUtc(PathOf(file), DateTime.UtcNow.AddDays(-2));

        using var scope = factory.Services.CreateScope();
        var removed = await OrphanFileSweeper.SweepAsync(scope.ServiceProvider.GetRequiredService<InventoryDbContext>(), scope.ServiceProvider.GetRequiredService<FileStore>(), DateTime.UtcNow - OrphanFileSweeper.GracePeriod);

        Assert.Equal(1, removed);
        Assert.True(File.Exists(PathOf(kept)));
        Assert.False(File.Exists(PathOf(orphan)));
        Assert.True(File.Exists(PathOf(fresh))); // too new: may still be waiting to be attached
    }

    [Fact]
    public async Task NetworkAccess_RequiresHomeNetworkEnabledSettingAndPin()
    {
        using var factory = new CustomWebApplicationFactory();
        using var local = CreateClient(factory);
        using var phone = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("http://localhost"), AllowAutoRedirect = false, HandleCookies = true });
        phone.DefaultRequestHeaders.Add(CustomWebApplicationFactory.RemoteIpHeader, "192.168.1.50");
        using var outsider = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("http://localhost"), AllowAutoRedirect = false });
        outsider.DefaultRequestHeaders.Add(CustomWebApplicationFactory.RemoteIpHeader, "203.0.113.9");

        // This computer always works; other devices are refused while network access is off.
        Assert.Equal(HttpStatusCode.OK, (await local.GetAsync("/api/properties")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await phone.GetAsync("/api/properties")).StatusCode);

        // Settings can only be changed from this computer, and a PIN is required.
        Assert.Equal(HttpStatusCode.Forbidden, (await phone.PutAsJsonAsync("/api/network", new NetworkSettingsInput(true, "2468", null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await local.PutAsJsonAsync("/api/network", new NetworkSettingsInput(true, null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await local.PutAsJsonAsync("/api/network", new NetworkSettingsInput(true, "12", null))).StatusCode);
        var status = (await (await local.PutAsJsonAsync("/api/network", new NetworkSettingsInput(true, "2468", null))).Content.ReadFromJsonAsync<NetworkStatusDto>())!;
        Assert.True(status.Enabled && status.HasPin);
        Assert.DoesNotContain("2468", await File.ReadAllTextAsync(Path.Combine(factory.FilesPath, "network.json"))); // stored hashed

        // Public addresses are never allowed; home-network devices must sign in with the PIN.
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync("/api/properties")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await phone.GetAsync("/api/properties")).StatusCode);
        var page = await phone.GetAsync("/assets");
        Assert.Equal(HttpStatusCode.Redirect, page.StatusCode);
        Assert.StartsWith("/pin", page.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SignInAsync(phone, "0000")).StatusCode);
        var signIn = await SignInAsync(phone, "2468", "/assets");
        Assert.Equal(HttpStatusCode.Redirect, signIn.StatusCode);
        Assert.Equal("/assets", signIn.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.OK, (await phone.GetAsync("/api/properties")).StatusCode);

        // A new PIN signs every device out.
        await local.PutAsJsonAsync("/api/network", new NetworkSettingsInput(true, "13579", null));
        Assert.Equal(HttpStatusCode.Unauthorized, (await phone.GetAsync("/api/properties")).StatusCode);

        // Turning access off blocks devices again, even with a valid cookie.
        await SignInAsync(phone, "13579");
        await local.PutAsJsonAsync("/api/network", new NetworkSettingsInput(false, null, null));
        Assert.Equal(HttpStatusCode.Forbidden, (await phone.GetAsync("/api/properties")).StatusCode);
    }

    [Fact]
    public async Task NetworkAccess_LocksOutAfterRepeatedWrongPins_AndIgnoresOffSiteReturnUrls()
    {
        using var factory = new CustomWebApplicationFactory();
        using var local = CreateClient(factory);
        await local.PutAsJsonAsync("/api/network", new NetworkSettingsInput(true, "2468", null));
        using var phone = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("http://localhost"), AllowAutoRedirect = false });
        phone.DefaultRequestHeaders.Add(CustomWebApplicationFactory.RemoteIpHeader, "10.0.0.77");

        var offSite = await SignInAsync(phone, "2468", "//evil.example/");
        Assert.Equal("/", offSite.Headers.Location!.OriginalString);
        for (var attempt = 0; attempt < 5; attempt++) await SignInAsync(phone, "9999");
        Assert.Equal((HttpStatusCode)429, (await SignInAsync(phone, "2468")).StatusCode); // even the right PIN waits out the lockout
    }

    [Fact]
    public async Task Thumbnails_AreSmallJpegsCachedAndRemovedWithThePhoto()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = CreateClient(factory);
        var property = await CreatePropertyAsync(client);
        using var source = new ImageMagick.MagickImage(ImageMagick.MagickColors.SteelBlue, 1200, 800);
        var photo = await UploadAsync(client, source.ToByteArray(ImageMagick.MagickFormat.Png), "lounge.png");
        var record = (await (await client.PostAsJsonAsync($"/api/properties/{property.Id}/photos", new PhotoMetadataInput(photo.StorageKey, null, 0))).Content.ReadFromJsonAsync<PhotoMetadataDto>())!;

        var thumbnail = await client.GetAsync($"/api/files/{photo.StorageKey}?size=thumb");
        Assert.Equal("image/jpeg", thumbnail.Content.Headers.ContentType?.MediaType);
        using (var image = new ImageMagick.MagickImage(await thumbnail.Content.ReadAsByteArrayAsync()))
            Assert.Equal((360u, 240u), (image.Width, image.Height));
        var cached = Path.Combine(factory.FilesPath, photo.StorageKey) + ".thumb.jpg";
        Assert.True(File.Exists(cached));

        var pdf = await UploadAsync(client, PdfBytes, "manual.pdf");
        Assert.Equal("application/pdf", (await client.GetAsync($"/api/files/{pdf.StorageKey}?size=thumb")).Content.Headers.ContentType?.MediaType);

        await client.DeleteAsync($"/api/properties/{property.Id}/photos/{record.Id}");
        Assert.False(File.Exists(cached));
    }

    [Fact]
    public async Task HeicUploads_ThatCannotBeDecoded_AreKeptAsHeic()
    {
        // Magick.NET can read HEIC but not write it, so real conversion is checked manually; this covers the fallback.
        using var factory = new CustomWebApplicationFactory();
        using var client = CreateClient(factory);
        byte[] fakeHeic = [0, 0, 0, 24, .. "ftypheic"u8, 0, 0, 0, 0, .. "mif1heic"u8, 1, 2, 3, 4];

        var uploaded = await UploadAsync(client, fakeHeic, "IMG_0002.HEIC");

        Assert.EndsWith(".heic", uploaded.StorageKey);
        Assert.Equal(fakeHeic, await client.GetByteArrayAsync($"/api/files/{uploaded.StorageKey}"));
        Assert.Equal("image/heic", (await client.GetAsync($"/api/files/{uploaded.StorageKey}?size=thumb")).Content.Headers.ContentType?.MediaType); // no thumbnail possible: original served
    }

    private static Task<HttpResponseMessage> SignInAsync(HttpClient client, string pin, string returnUrl = "/") =>
        client.PostAsync("/pin", new FormUrlEncodedContent(new Dictionary<string, string> { ["pin"] = pin, ["returnUrl"] = returnUrl }));

    private static async Task<HttpResponseMessage> PostFileAsync(HttpClient client, byte[] bytes, string fileName)
    {
        using var content = new MultipartFormDataContent { { new ByteArrayContent(bytes), "file", fileName } };
        return await client.PostAsync("/api/files", content);
    }

    private static async Task<UploadedFileDto> UploadAsync(HttpClient client, byte[] bytes, string fileName)
    {
        var response = await PostFileAsync(client, bytes, fileName);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<UploadedFileDto>())!;
    }

    private static async Task<MaintenanceTaskDto> CreateTaskAsync(HttpClient client, MaintenanceTaskInput input)
    {
        var response = await client.PostAsJsonAsync("/api/maintenance", input);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<MaintenanceTaskDto>())!;
    }

    private static SurfaceInput SurfaceFor(Guid roomId, string name, string type) =>
        new(roomId, name, type, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, 0);

    private static FixtureInput FixtureFor(Guid roomId) =>
        new(roomId, "Sink", "Sink", null, null, null, null, null, null, null, null, null, null, null, null);

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

    private static async Task<RoomDto> CreateRoomAsync(HttpClient client, Guid floorId, string name = "Lounge")
    {
        var response = await client.PostAsJsonAsync("/api/rooms", new RoomInput(floorId, name, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null));
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
