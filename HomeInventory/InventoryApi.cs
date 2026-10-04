using System.IO.Compression;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using HomeInventory.Client;
using Microsoft.EntityFrameworkCore;
using QRCoder;

namespace HomeInventory;

public static class InventoryApi
{
    public static RouteGroupBuilder MapInventoryApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");
        api.MapGet("/properties", async (InventoryDbContext db) => await db.Properties.OrderBy(x => x.Name).Select(x => ToDto(x)).ToListAsync());
        api.MapPost("/properties", async (PropertyInput input, InventoryDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(input.Name)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Name is required."] });
            var currency = NormalizeCurrency(input.Currency);
            if (currency is null) return Results.ValidationProblem(new Dictionary<string, string[]> { ["currency"] = ["Currency must be a three-letter ISO code."] });
            var entity = new Property { Name = input.Name.Trim(), Address = input.Address?.Trim(), PurchaseDate = input.PurchaseDate, PurchasePrice = input.PurchasePrice, FloorArea = input.FloorArea, Currency = currency, Notes = input.Notes?.Trim() };
            db.Properties.Add(entity); await db.SaveChangesAsync(); return Results.Created($"/api/properties/{entity.Id}", ToDto(entity));
        });
        api.MapPut("/properties/{id:guid}", async (Guid id, PropertyInput input, InventoryDbContext db) =>
        {
            var entity = await db.Properties.FindAsync(id); if (entity is null) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(input.Name)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Name is required."] });
            var currency = NormalizeCurrency(input.Currency);
            if (currency is null) return Results.ValidationProblem(new Dictionary<string, string[]> { ["currency"] = ["Currency must be a three-letter ISO code."] });
            entity.Name = input.Name.Trim(); entity.Address = input.Address?.Trim(); entity.PurchaseDate = input.PurchaseDate; entity.PurchasePrice = input.PurchasePrice; entity.FloorArea = input.FloorArea; entity.Currency = currency; entity.Notes = input.Notes?.Trim(); await db.SaveChangesAsync(); return Results.Ok(ToDto(entity));
        });
        api.MapDelete("/properties/{id:guid}", async (Guid id, InventoryDbContext db, FileStore store) =>
        {
            var entity = await db.Properties.FindAsync(id);
            if (entity is null) return Results.NotFound();
            // Assets are never deleted (archived ones included), so a property holding any must have them reassigned first.
            var assetCount = await db.Assets.CountAsync(x => x.PropertyId == id);
            if (assetCount > 0) return Results.BadRequest($"This property still has {assetCount} asset(s), including archived ones. Reassign them to another property before deleting it.");
            // StorageLocation.Parent is Restrict, so remove the location tree leaf-first before the property cascade runs.
            var locations = await db.StorageLocations.Where(x => x.PropertyId == id).ToListAsync();
            while (locations.Count > 0)
            {
                var leaves = locations.Where(x => !locations.Any(y => y.ParentId == x.Id)).ToList();
                db.StorageLocations.RemoveRange(leaves);
                await db.SaveChangesAsync();
                locations = locations.Except(leaves).ToList();
            }
            var fileKeys = await FileKeysFor(db, propertyId: id);
            db.Properties.Remove(entity);
            await db.SaveChangesAsync();
            await DeleteUnusedFiles(db, store, fileKeys);
            return Results.NoContent();
        });

        api.MapGet("/properties/{id:guid}/floors", async (Guid id, InventoryDbContext db) =>
        {
            if (!await db.Properties.AnyAsync(x => x.Id == id)) return Results.NotFound();
            return Results.Ok(await db.Floors.Where(x => x.PropertyId == id).OrderBy(x => x.Name).Select(x => ToDto(x)).ToListAsync());
        });
        api.MapPost("/properties/{id:guid}/floors", async (Guid id, FloorInput input, InventoryDbContext db) =>
        {
            if (!await db.Properties.AnyAsync(x => x.Id == id)) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(input.Name)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Name is required."] });
            var entity = new Floor { PropertyId = id, Name = input.Name.Trim(), Notes = input.Notes?.Trim() };
            db.Floors.Add(entity);
            await db.SaveChangesAsync();
            return Results.Created($"/api/floors/{entity.Id}", ToDto(entity));
        });
        api.MapGet("/floors", async (Guid? propertyId, InventoryDbContext db) =>
        {
            var floors = db.Floors.AsQueryable();
            if (propertyId is not null) floors = floors.Where(x => x.PropertyId == propertyId.Value);
            return Results.Ok(await floors.OrderBy(x => x.Name).Select(x => ToDto(x)).ToListAsync());
        });
        api.MapGet("/floors/{id:guid}", async (Guid id, InventoryDbContext db) =>
        {
            var entity = await db.Floors.FindAsync(id);
            return entity is null ? Results.NotFound() : Results.Ok(ToDto(entity));
        });
        api.MapPut("/floors/{id:guid}", async (Guid id, FloorInput input, InventoryDbContext db) =>
        {
            var entity = await db.Floors.FindAsync(id);
            if (entity is null) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(input.Name)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Name is required."] });
            entity.Name = input.Name.Trim();
            entity.Notes = input.Notes?.Trim();
            await db.SaveChangesAsync();
            return Results.Ok(ToDto(entity));
        });
        api.MapDelete("/floors/{id:guid}", async (Guid id, InventoryDbContext db, FileStore store) =>
        {
            var entity = await db.Floors.FindAsync(id);
            if (entity is null) return Results.NotFound();
            var assetCount = await db.Assets.CountAsync(x => x.Room != null && x.Room.FloorId == id);
            if (assetCount > 0) return Results.BadRequest($"This floor still has {assetCount} asset(s) in its rooms. Move them before deleting the floor.");
            var fileKeys = await FileKeysFor(db, floorId: id);
            db.Floors.Remove(entity);
            await db.SaveChangesAsync();
            await DeleteUnusedFiles(db, store, fileKeys);
            return Results.NoContent();
        });

        api.MapGet("/rooms", async (Guid? floorId, Guid? propertyId, InventoryDbContext db) =>
        {
            var rooms = db.Rooms.AsQueryable();
            if (floorId is not null) rooms = rooms.Where(x => x.FloorId == floorId.Value);
            else if (propertyId is not null) rooms = rooms.Where(x => x.Floor != null && x.Floor.PropertyId == propertyId.Value);
            return Results.Ok(await rooms.OrderBy(x => x.Name).Select(x => ToDto(x)).ToListAsync());
        });
        api.MapGet("/floors/{id:guid}/rooms", async (Guid id, InventoryDbContext db) =>
        {
            if (!await db.Floors.AnyAsync(x => x.Id == id)) return Results.NotFound();
            return Results.Ok(await db.Rooms.Where(x => x.FloorId == id).OrderBy(x => x.Name).Select(x => ToDto(x)).ToListAsync());
        });
        api.MapPost("/rooms", async (RoomInput input, InventoryDbContext db) =>
        {
            if (!await db.Floors.AnyAsync(x => x.Id == input.FloorId)) return Results.BadRequest("The selected floor does not exist.");
            if (string.IsNullOrWhiteSpace(input.Name)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Name is required."] });
            var floor = await db.Floors.FindAsync(input.FloorId);
            var entity = new Room
            {
                PropertyId = floor!.PropertyId,
                FloorId = input.FloorId,
                Name = input.Name.Trim(),
                Type = input.Type?.Trim(),
                Area = input.Area,
                Volume = input.Volume,
                CeilingHeight = input.CeilingHeight,
                Length = input.Length,
                Width = input.Width,
                Height = input.Height,
                Flooring = input.Flooring?.Trim(),
                WallFinish = input.WallFinish?.Trim(),
                CeilingFinish = input.CeilingFinish?.Trim(),
                PaintDetails = input.PaintDetails?.Trim(),
                WindowsCount = input.WindowsCount,
                DoorsCount = input.DoorsCount,
                FixturesNotes = input.FixturesNotes?.Trim(),
                UtilitiesNotes = input.UtilitiesNotes?.Trim(),
                Notes = input.Notes?.Trim()
            };
            db.Rooms.Add(entity);
            await db.SaveChangesAsync();
            return Results.Created($"/api/rooms/{entity.Id}", ToDto(entity));
        });
        api.MapPut("/rooms/{id:guid}", async (Guid id, RoomInput input, InventoryDbContext db) =>
        {
            var entity = await db.Rooms.FindAsync(id);
            if (entity is null) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(input.Name) || !await db.Floors.AnyAsync(x => x.Id == input.FloorId)) return Results.BadRequest("A valid floor and room name are required.");
            var newPropertyId = (await db.Floors.FindAsync(input.FloorId))!.PropertyId;
            if (newPropertyId != entity.PropertyId)
            {
                // Assets are owned by a property, so a room holding them can't move to another property.
                if (await db.Assets.AnyAsync(x => x.RoomId == id)) return Results.BadRequest("Move this room's assets out before moving it to a floor in another property.");
                await db.MaintenanceTasks.Where(x => x.Fixture != null && x.Fixture.RoomId == id).ExecuteUpdateAsync(x => x.SetProperty(t => t.PropertyId, newPropertyId));
            }
            entity.FloorId = input.FloorId;
            entity.PropertyId = newPropertyId;
            entity.Name = input.Name.Trim();
            entity.Type = input.Type?.Trim();
            entity.Area = input.Area;
            entity.Volume = input.Volume;
            entity.CeilingHeight = input.CeilingHeight;
            entity.Length = input.Length;
            entity.Width = input.Width;
            entity.Height = input.Height;
            entity.Flooring = input.Flooring?.Trim();
            entity.WallFinish = input.WallFinish?.Trim();
            entity.CeilingFinish = input.CeilingFinish?.Trim();
            entity.PaintDetails = input.PaintDetails?.Trim();
            entity.WindowsCount = input.WindowsCount;
            entity.DoorsCount = input.DoorsCount;
            entity.FixturesNotes = input.FixturesNotes?.Trim();
            entity.UtilitiesNotes = input.UtilitiesNotes?.Trim();
            entity.Notes = input.Notes?.Trim();
            await db.SaveChangesAsync();
            return Results.Ok(ToDto(entity));
        });
        api.MapDelete("/rooms/{id:guid}", async (Guid id, InventoryDbContext db, FileStore store) =>
        {
            var entity = await db.Rooms.FindAsync(id);
            if (entity is null) return Results.NotFound();
            var assetCount = await db.Assets.CountAsync(x => x.RoomId == id);
            if (assetCount > 0) return Results.BadRequest($"This room still holds {assetCount} asset(s). Move them before deleting the room.");
            var fileKeys = await FileKeysFor(db, roomId: id);
            db.Rooms.Remove(entity);
            await db.SaveChangesAsync();
            await DeleteUnusedFiles(db, store, fileKeys);
            return Results.NoContent();
        });

        api.MapGet("/rooms/{id:guid}/surfaces", async (Guid id, InventoryDbContext db) =>
        {
            if (!await db.Rooms.AnyAsync(x => x.Id == id)) return Results.NotFound();
            return Results.Ok(await db.Surfaces.Where(x => x.RoomId == id).OrderBy(x => x.SortOrder).ThenBy(x => x.Name).Select(x => ToDto(x)).ToListAsync());
        });
        api.MapPost("/rooms/{id:guid}/surfaces", async (Guid id, SurfaceInput input, InventoryDbContext db) =>
        {
            if (!await db.Rooms.AnyAsync(x => x.Id == id)) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(input.Name)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Name is required."] });
            var entity = new Surface
            {
                RoomId = id,
                Name = input.Name.Trim(),
                SurfaceType = input.SurfaceType.Trim(),
                PaintBrand = input.PaintBrand?.Trim(),
                ColorName = input.ColorName?.Trim(),
                ColorCode = input.ColorCode?.Trim(),
                Finish = input.Finish?.Trim(),
                Coats = input.Coats,
                PaintedDate = input.PaintedDate,
                Painter = input.Painter?.Trim(),
                QuantityPurchased = input.QuantityPurchased,
                Manufacturer = input.Manufacturer?.Trim(),
                ProductName = input.ProductName?.Trim(),
                Material = input.Material?.Trim(),
                Supplier = input.Supplier?.Trim(),
                Warranty = input.Warranty?.Trim(),
                Invoice = input.Invoice?.Trim(),
                InstallationDate = input.InstallationDate,
                Notes = input.Notes?.Trim(),
                SortOrder = input.SortOrder
            };
            db.Surfaces.Add(entity);
            await db.SaveChangesAsync();
            return Results.Created($"/api/surfaces/{entity.Id}", ToDto(entity));
        });
        api.MapPut("/surfaces/{id:guid}", async (Guid id, SurfaceInput input, InventoryDbContext db) =>
        {
            var entity = await db.Surfaces.FindAsync(id);
            if (entity is null) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(input.Name)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Name is required."] });
            entity.Name = input.Name.Trim();
            entity.SurfaceType = input.SurfaceType.Trim();
            entity.PaintBrand = input.PaintBrand?.Trim();
            entity.ColorName = input.ColorName?.Trim();
            entity.ColorCode = input.ColorCode?.Trim();
            entity.Finish = input.Finish?.Trim();
            entity.Coats = input.Coats;
            entity.PaintedDate = input.PaintedDate;
            entity.Painter = input.Painter?.Trim();
            entity.QuantityPurchased = input.QuantityPurchased;
            entity.Manufacturer = input.Manufacturer?.Trim();
            entity.ProductName = input.ProductName?.Trim();
            entity.Material = input.Material?.Trim();
            entity.Supplier = input.Supplier?.Trim();
            entity.Warranty = input.Warranty?.Trim();
            entity.Invoice = input.Invoice?.Trim();
            entity.InstallationDate = input.InstallationDate;
            entity.Notes = input.Notes?.Trim();
            entity.SortOrder = input.SortOrder;
            await db.SaveChangesAsync();
            return Results.Ok(ToDto(entity));
        });
        api.MapDelete("/surfaces/{id:guid}", async (Guid id, InventoryDbContext db) =>
        {
            var entity = await db.Surfaces.FindAsync(id);
            if (entity is null) return Results.NotFound();
            db.Surfaces.Remove(entity);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        api.MapGet("/rooms/{id:guid}/fixtures", async (Guid id, InventoryDbContext db) =>
        {
            if (!await db.Rooms.AnyAsync(x => x.Id == id)) return Results.NotFound();
            return Results.Ok(await FixtureDtos(db, db.Fixtures.Where(x => x.RoomId == id)));
        });
        api.MapPost("/rooms/{id:guid}/fixtures", async (Guid id, FixtureInput input, InventoryDbContext db) =>
        {
            if (!await db.Rooms.AnyAsync(x => x.Id == id)) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(input.Name) || string.IsNullOrWhiteSpace(input.Type))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Name is required."], ["type"] = ["Type is required."] });
            var entity = new Fixture
            {
                RoomId = id,
                Name = input.Name.Trim(),
                Type = input.Type.Trim(),
                Manufacturer = input.Manufacturer?.Trim(),
                Model = input.Model?.Trim(),
                SerialNumber = input.SerialNumber?.Trim(),
                PurchaseDate = input.PurchaseDate,
                PurchasePrice = input.PurchasePrice,
                CurrentValue = input.CurrentValue,
                Warranty = input.Warranty?.Trim(),
                ManualUrl = input.ManualUrl?.Trim(),
                InstallerName = input.InstallerName?.Trim(),
                InstallationDate = input.InstallationDate,
                MaintenanceSchedule = input.MaintenanceSchedule?.Trim(),
                LastMaintenanceDate = input.LastMaintenanceDate,
                Condition = input.Condition?.Trim(),
                Notes = input.Notes?.Trim(),
                Category = FixtureCategory(input.Category),
                Provider = input.Provider?.Trim(),
                AccountNumber = input.AccountNumber?.Trim()
            };
            db.Fixtures.Add(entity);
            await db.SaveChangesAsync();
            return Results.Created($"/api/fixtures/{entity.Id}", (await FixtureDtos(db, db.Fixtures.Where(x => x.Id == entity.Id))).Single());
        });
        api.MapGet("/fixtures", async (Guid? roomId, Guid? propertyId, string? category, InventoryDbContext db) =>
        {
            var fixtures = db.Fixtures.AsQueryable();
            if (!string.IsNullOrWhiteSpace(category)) { var normalized = FixtureCategory(category); fixtures = fixtures.Where(x => x.Category == normalized); }
            if (roomId is not null) fixtures = fixtures.Where(x => x.RoomId == roomId.Value);
            else if (propertyId is not null) fixtures = fixtures.Where(x => x.Room != null && x.Room.Floor != null && x.Room.Floor.PropertyId == propertyId.Value);
            return Results.Ok(await FixtureDtos(db, fixtures));
        });
        api.MapGet("/fixtures/{id:guid}", async (Guid id, InventoryDbContext db) =>
        {
            var entity = await db.Fixtures.FindAsync(id);
            return entity is null ? Results.NotFound() : Results.Ok((await FixtureDtos(db, db.Fixtures.Where(x => x.Id == entity.Id))).Single());
        });
        api.MapPut("/fixtures/{id:guid}", async (Guid id, FixtureInput input, InventoryDbContext db) =>
        {
            var entity = await db.Fixtures.FindAsync(id);
            if (entity is null) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(input.Name) || string.IsNullOrWhiteSpace(input.Type))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Name is required."], ["type"] = ["Type is required."] });
            var roomPropertyId = await db.Rooms.Where(x => x.Id == input.RoomId).Select(x => (Guid?)x.PropertyId).SingleOrDefaultAsync();
            if (roomPropertyId is null) return Results.BadRequest("The selected room does not exist.");
            // Maintenance tasks follow their fixture if it moves to a room in another property.
            await db.MaintenanceTasks.Where(x => x.FixtureId == id && x.PropertyId != roomPropertyId).ExecuteUpdateAsync(x => x.SetProperty(t => t.PropertyId, roomPropertyId.Value));
            entity.RoomId = input.RoomId;
            entity.Name = input.Name.Trim();
            entity.Type = input.Type.Trim();
            entity.Manufacturer = input.Manufacturer?.Trim();
            entity.Model = input.Model?.Trim();
            entity.SerialNumber = input.SerialNumber?.Trim();
            entity.PurchaseDate = input.PurchaseDate;
            entity.PurchasePrice = input.PurchasePrice;
            entity.CurrentValue = input.CurrentValue;
            entity.Warranty = input.Warranty?.Trim();
            entity.ManualUrl = input.ManualUrl?.Trim();
            entity.InstallerName = input.InstallerName?.Trim();
            entity.InstallationDate = input.InstallationDate;
            entity.MaintenanceSchedule = input.MaintenanceSchedule?.Trim();
            entity.LastMaintenanceDate = input.LastMaintenanceDate;
            entity.Condition = input.Condition?.Trim();
            entity.Notes = input.Notes?.Trim();
            entity.Category = FixtureCategory(input.Category);
            entity.Provider = input.Provider?.Trim();
            entity.AccountNumber = input.AccountNumber?.Trim();
            await db.SaveChangesAsync();
            return Results.Ok((await FixtureDtos(db, db.Fixtures.Where(x => x.Id == entity.Id))).Single());
        });
        api.MapDelete("/fixtures/{id:guid}", async (Guid id, InventoryDbContext db, FileStore store) =>
        {
            var entity = await db.Fixtures.FindAsync(id);
            if (entity is null) return Results.NotFound();
            var fileKeys = await FileKeysFor(db, fixtureId: id);
            db.Fixtures.Remove(entity);
            await db.SaveChangesAsync();
            await DeleteUnusedFiles(db, store, fileKeys);
            return Results.NoContent();
        });
        api.MapGet("/fixtures/{id:guid}/photos", async (Guid id, InventoryDbContext db) =>
        {
            if (!await db.Fixtures.AnyAsync(x => x.Id == id)) return Results.NotFound();
            return Results.Ok(await db.FixturePhotos.Where(x => x.FixtureId == id).OrderBy(x => x.SortOrder).ThenBy(x => x.Id).Select(x => ToDto(x)).ToListAsync());
        });
        api.MapPost("/fixtures/{id:guid}/photos", async (Guid id, FixturePhotoInput input, InventoryDbContext db) =>
        {
            if (!await db.Fixtures.AnyAsync(x => x.Id == id)) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(input.StorageKey)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["storageKey"] = ["Storage key is required."] });
            var entity = new FixturePhoto { FixtureId = id, StorageKey = input.StorageKey.Trim(), Caption = input.Caption?.Trim(), SortOrder = input.SortOrder };
            db.FixturePhotos.Add(entity);
            await db.SaveChangesAsync();
            return Results.Created($"/api/fixtures/{id}/photos/{entity.Id}", ToDto(entity));
        });
        api.MapDelete("/fixtures/{id:guid}/photos/{photoId:guid}", async (Guid id, Guid photoId, InventoryDbContext db, FileStore store) =>
        {
            var entity = await db.FixturePhotos.SingleOrDefaultAsync(x => x.Id == photoId && x.FixtureId == id);
            if (entity is null) return Results.NotFound();
            db.FixturePhotos.Remove(entity);
            await db.SaveChangesAsync();
            await DeleteUnusedFiles(db, store, [entity.StorageKey]);
            return Results.NoContent();
        });

        api.MapGet("/paints", async (InventoryDbContext db) => Results.Ok(await db.Paints.OrderBy(x => x.Brand).ThenBy(x => x.ColorName).Select(x => ToDto(x)).ToListAsync()));
        api.MapPost("/paints", async (PaintInput input, InventoryDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(input.Brand) || string.IsNullOrWhiteSpace(input.ColorName))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["brand"] = ["Brand is required."], ["colorName"] = ["Color name is required."] });
            var entity = new Paint { Brand = input.Brand.Trim(), ColorName = input.ColorName.Trim(), ColorCode = input.ColorCode?.Trim(), Finish = input.Finish?.Trim(), Notes = input.Notes?.Trim() };
            db.Paints.Add(entity); await db.SaveChangesAsync(); return Results.Created($"/api/paints/{entity.Id}", ToDto(entity));
        });
        api.MapPut("/paints/{id:guid}", async (Guid id, PaintInput input, InventoryDbContext db) =>
        {
            var entity = await db.Paints.FindAsync(id); if (entity is null) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(input.Brand) || string.IsNullOrWhiteSpace(input.ColorName))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["brand"] = ["Brand is required."], ["colorName"] = ["Color name is required."] });
            entity.Brand = input.Brand.Trim(); entity.ColorName = input.ColorName.Trim(); entity.ColorCode = input.ColorCode?.Trim(); entity.Finish = input.Finish?.Trim(); entity.Notes = input.Notes?.Trim();
            await db.SaveChangesAsync(); return Results.Ok(ToDto(entity));
        });
        api.MapDelete("/paints/{id:guid}", async (Guid id, InventoryDbContext db) =>
        {
            var entity = await db.Paints.FindAsync(id); if (entity is null) return Results.NotFound();
            db.Paints.Remove(entity); await db.SaveChangesAsync(); return Results.NoContent();
        });
        api.MapGet("/rooms/{id:guid}/paints", async (Guid id, InventoryDbContext db) =>
        {
            if (!await db.Rooms.AnyAsync(x => x.Id == id)) return Results.NotFound();
            return Results.Ok(await RoomPaintDtos(db, id));
        });
        api.MapPost("/rooms/{id:guid}/paints", async (Guid id, RoomPaintInput input, InventoryDbContext db) =>
        {
            if (!await db.Rooms.AnyAsync(x => x.Id == id)) return Results.NotFound();
            if (!await db.Paints.AnyAsync(x => x.Id == input.PaintId)) return Results.BadRequest("The selected paint does not exist.");
            if (await db.RoomPaints.AnyAsync(x => x.RoomId == id && x.PaintId == input.PaintId)) return Results.BadRequest("That paint is already assigned to the room.");
            db.RoomPaints.Add(new RoomPaint { RoomId = id, PaintId = input.PaintId, SortOrder = input.SortOrder, Surface = input.Surface?.Trim() });
            await db.SaveChangesAsync(); return Results.Ok(await RoomPaintDtos(db, id));
        });
        api.MapPut("/rooms/{id:guid}/paints/{paintId:guid}", async (Guid id, Guid paintId, RoomPaintInput input, InventoryDbContext db) =>
        {
            var entity = await db.RoomPaints.SingleOrDefaultAsync(x => x.RoomId == id && x.PaintId == paintId); if (entity is null) return Results.NotFound();
            if (paintId != input.PaintId) return Results.BadRequest("Paint id in route and payload must match.");
            entity.SortOrder = input.SortOrder; entity.Surface = input.Surface?.Trim(); await db.SaveChangesAsync(); return Results.Ok(await RoomPaintDtos(db, id));
        });
        api.MapDelete("/rooms/{id:guid}/paints/{paintId:guid}", async (Guid id, Guid paintId, InventoryDbContext db) =>
        {
            var entity = await db.RoomPaints.SingleOrDefaultAsync(x => x.RoomId == id && x.PaintId == paintId); if (entity is null) return Results.NotFound();
            db.RoomPaints.Remove(entity); await db.SaveChangesAsync(); return Results.NoContent();
        });
        api.MapGet("/paints/{id:guid}/usage", async (Guid id, InventoryDbContext db) =>
        {
            var paint = await db.Paints.FindAsync(id);
            if (paint is null) return Results.NotFound();
            return Results.Ok(await PaintUsage(db, paint));
        });
        api.MapGet("/properties/{id:guid}/photos", async (Guid id, InventoryDbContext db) =>
        {
            if (!await db.Properties.AnyAsync(x => x.Id == id)) return Results.NotFound();
            return Results.Ok(await db.PropertyPhotos.Where(x => x.PropertyId == id).OrderBy(x => x.SortOrder).ThenBy(x => x.Id).Select(x => ToDto(x)).ToListAsync());
        });
        api.MapPost("/properties/{id:guid}/photos", async (Guid id, PhotoMetadataInput input, InventoryDbContext db) =>
        {
            if (!await db.Properties.AnyAsync(x => x.Id == id)) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(input.StorageKey)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["storageKey"] = ["Storage key is required."] });
            var entity = new PropertyPhoto { PropertyId = id, StorageKey = input.StorageKey.Trim(), Caption = input.Caption?.Trim(), SortOrder = input.SortOrder };
            db.PropertyPhotos.Add(entity); await db.SaveChangesAsync(); return Results.Created($"/api/properties/{id}/photos/{entity.Id}", ToDto(entity));
        });
        api.MapPut("/properties/{id:guid}/photos/{photoId:guid}", async (Guid id, Guid photoId, PhotoMetadataInput input, InventoryDbContext db, FileStore store) =>
        {
            var entity = await db.PropertyPhotos.SingleOrDefaultAsync(x => x.Id == photoId && x.PropertyId == id); if (entity is null) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(input.StorageKey)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["storageKey"] = ["Storage key is required."] });
            var oldKey = entity.StorageKey;
            entity.StorageKey = input.StorageKey.Trim(); entity.Caption = input.Caption?.Trim(); entity.SortOrder = input.SortOrder; await db.SaveChangesAsync();
            if (oldKey != entity.StorageKey) await DeleteUnusedFiles(db, store, [oldKey]);
            return Results.Ok(ToDto(entity));
        });
        api.MapDelete("/properties/{id:guid}/photos/{photoId:guid}", async (Guid id, Guid photoId, InventoryDbContext db, FileStore store) =>
        {
            var entity = await db.PropertyPhotos.SingleOrDefaultAsync(x => x.Id == photoId && x.PropertyId == id); if (entity is null) return Results.NotFound();
            db.PropertyPhotos.Remove(entity); await db.SaveChangesAsync();
            await DeleteUnusedFiles(db, store, [entity.StorageKey]);
            return Results.NoContent();
        });
        api.MapGet("/rooms/{id:guid}/photos", async (Guid id, InventoryDbContext db) =>
        {
            if (!await db.Rooms.AnyAsync(x => x.Id == id)) return Results.NotFound();
            return Results.Ok(await db.RoomPhotos.Where(x => x.RoomId == id).OrderBy(x => x.SortOrder).ThenBy(x => x.Id).Select(x => ToDto(x)).ToListAsync());
        });
        api.MapPost("/rooms/{id:guid}/photos", async (Guid id, PhotoMetadataInput input, InventoryDbContext db) =>
        {
            if (!await db.Rooms.AnyAsync(x => x.Id == id)) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(input.StorageKey)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["storageKey"] = ["Storage key is required."] });
            var entity = new RoomPhoto { RoomId = id, StorageKey = input.StorageKey.Trim(), Caption = input.Caption?.Trim(), SortOrder = input.SortOrder };
            db.RoomPhotos.Add(entity); await db.SaveChangesAsync(); return Results.Created($"/api/rooms/{id}/photos/{entity.Id}", ToDto(entity));
        });
        api.MapPut("/rooms/{id:guid}/photos/{photoId:guid}", async (Guid id, Guid photoId, PhotoMetadataInput input, InventoryDbContext db, FileStore store) =>
        {
            var entity = await db.RoomPhotos.SingleOrDefaultAsync(x => x.Id == photoId && x.RoomId == id); if (entity is null) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(input.StorageKey)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["storageKey"] = ["Storage key is required."] });
            var oldKey = entity.StorageKey;
            entity.StorageKey = input.StorageKey.Trim(); entity.Caption = input.Caption?.Trim(); entity.SortOrder = input.SortOrder; await db.SaveChangesAsync();
            if (oldKey != entity.StorageKey) await DeleteUnusedFiles(db, store, [oldKey]);
            return Results.Ok(ToDto(entity));
        });
        api.MapDelete("/rooms/{id:guid}/photos/{photoId:guid}", async (Guid id, Guid photoId, InventoryDbContext db, FileStore store) =>
        {
            var entity = await db.RoomPhotos.SingleOrDefaultAsync(x => x.Id == photoId && x.RoomId == id); if (entity is null) return Results.NotFound();
            db.RoomPhotos.Remove(entity); await db.SaveChangesAsync();
            await DeleteUnusedFiles(db, store, [entity.StorageKey]);
            return Results.NoContent();
        });

        api.MapGet("/locations", async (Guid? propertyId, InventoryDbContext db) => await LocationDtos(db, propertyId));
        api.MapPost("/locations", async (StorageLocationInput input, InventoryDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(input.Name)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Name is required."] });
            if (!await db.Properties.AnyAsync(x => x.Id == input.PropertyId)) return Results.BadRequest("The selected property does not exist.");
            if (input.ParentId is not null && !await db.StorageLocations.AnyAsync(x => x.Id == input.ParentId && x.PropertyId == input.PropertyId)) return Results.BadRequest("The parent storage location must be in the selected property.");
            var entity = new StorageLocation { PropertyId = input.PropertyId, ParentId = input.ParentId, Name = input.Name.Trim(), Type = input.Type?.Trim() };
            db.StorageLocations.Add(entity); await db.SaveChangesAsync(); return Results.Created($"/api/locations/{entity.Id}", (await LocationDtos(db, input.PropertyId)).Single(x => x.Id == entity.Id));
        });
        api.MapPut("/locations/{id:guid}", async (Guid id, StorageLocationInput input, InventoryDbContext db) =>
        {
            var entity=await db.StorageLocations.FindAsync(id); if(entity is null)return Results.NotFound();
            if(string.IsNullOrWhiteSpace(input.Name) || !await db.Properties.AnyAsync(x=>x.Id==input.PropertyId)) return Results.BadRequest("A valid property and location name are required.");
            if(input.ParentId==id || input.ParentId is not null && !await db.StorageLocations.AnyAsync(x=>x.Id==input.ParentId && x.PropertyId==input.PropertyId)) return Results.BadRequest("Choose a different parent location in the selected property.");
            if(input.ParentId is not null && await IsDescendant(db, input.ParentId.Value, id)) return Results.BadRequest("A location cannot be moved inside one of its own sub-locations.");
            if(input.PropertyId!=entity.PropertyId && (await db.StorageLocations.AnyAsync(x=>x.ParentId==id) || await db.Assets.AnyAsync(x=>x.StorageLocationId==id))) return Results.BadRequest("Move this location's sub-locations and assets before changing its property.");
            entity.PropertyId=input.PropertyId;entity.ParentId=input.ParentId;entity.Name=input.Name.Trim();entity.Type=input.Type?.Trim();await db.SaveChangesAsync();return Results.Ok((await LocationDtos(db,input.PropertyId)).Single(x=>x.Id==id));
        });
        api.MapDelete("/locations/{id:guid}", async (Guid id, InventoryDbContext db) =>
        {
            var entity = await db.StorageLocations.FindAsync(id);
            if (entity is null) return Results.NotFound();
            if (await db.StorageLocations.AnyAsync(x => x.ParentId == id)) return Results.BadRequest("Delete or move this location's sub-locations first.");
            var assetCount = await db.Assets.CountAsync(x => x.StorageLocationId == id);
            if (assetCount > 0) return Results.BadRequest($"This location still holds {assetCount} asset(s). Move them before deleting the location.");
            db.StorageLocations.Remove(entity);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        api.MapGet("/assets", async (Guid? propertyId, bool archived, InventoryDbContext db) => await AssetDtos(db, propertyId, archived));
        api.MapPost("/assets", async (AssetInput input, InventoryDbContext db) =>
        {
            var error = await ValidateAsset(input, db);
            if (error is not null) return Results.BadRequest(error);
            var entity = NewAsset(input);
            db.Assets.Add(entity);
            db.AssetEvents.Add(new AssetEvent { AssetId = entity.Id, OccurredOn = entity.PurchaseDate ?? Today(), Kind = "Added", Description = entity.PurchaseDate is null ? "Added to the inventory" : "Bought", Cost = entity.PurchasePrice });
            await db.SaveChangesAsync();
            return Results.Created($"/api/assets/{entity.Id}", (await AssetDtos(db, input.PropertyId, false)).Single(x => x.Id == entity.Id));
        });
        api.MapPut("/assets/{id:guid}", async (Guid id, AssetInput input, InventoryDbContext db) =>
        {
            var entity = await db.Assets.FindAsync(id);
            if (entity is null) return Results.NotFound();
            var error = await ValidateAsset(input, db);
            if (error is not null) return Results.BadRequest(error);
            var (fromRoom, fromLocation) = (entity.RoomId, entity.StorageLocationId);
            Apply(input, entity);
            await RecordMove(db, entity, fromRoom, fromLocation);
            await db.SaveChangesAsync();
            return Results.Ok((await AssetDtos(db, input.PropertyId, entity.IsArchived)).Single(x => x.Id == id));
        });
        api.MapPost("/assets/{id:guid}/move", async (Guid id, AssetMoveInput input, InventoryDbContext db) =>
        {
            var entity = await db.Assets.FindAsync(id);
            if (entity is null) return Results.NotFound();
            if (input.RoomId is not null && input.StorageLocationId is not null) return Results.BadRequest("Choose either a room or a storage location, not both.");
            if (input.RoomId is not null && !await db.Rooms.AnyAsync(x => x.Id == input.RoomId && x.Floor != null && x.Floor.PropertyId == entity.PropertyId)) return Results.BadRequest("Room must be in the asset property.");
            if (input.StorageLocationId is not null && !await db.StorageLocations.AnyAsync(x => x.Id == input.StorageLocationId && x.PropertyId == entity.PropertyId)) return Results.BadRequest("Storage location must be in the asset property.");
            var (fromRoom, fromLocation) = (entity.RoomId, entity.StorageLocationId);
            entity.RoomId = input.RoomId;
            entity.StorageLocationId = input.StorageLocationId;
            await RecordMove(db, entity, fromRoom, fromLocation);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
        api.MapPost("/assets/{id:guid}/archive", async (Guid id, InventoryDbContext db) =>
        {
            var entity = await db.Assets.FindAsync(id);
            if (entity is null) return Results.NotFound();
            if (!entity.IsArchived) db.AssetEvents.Add(new AssetEvent { AssetId = id, OccurredOn = Today(), Kind = "Archived" });
            entity.IsArchived = true;
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
        api.MapPost("/assets/{id:guid}/unarchive", async (Guid id, InventoryDbContext db) =>
        {
            var entity = await db.Assets.FindAsync(id);
            if (entity is null) return Results.NotFound();
            if (entity.IsArchived) db.AssetEvents.Add(new AssetEvent { AssetId = id, OccurredOn = Today(), Kind = "Unarchived" });
            entity.IsArchived = false;
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
        api.MapGet("/assets/{id:guid}", async (Guid id, InventoryDbContext db) =>
        {
            var asset = await db.Assets.Where(x => x.Id == id).Select(x => new { x.PropertyId, x.IsArchived }).SingleOrDefaultAsync();
            return asset is null ? Results.NotFound() : Results.Ok((await AssetDtos(db, asset.PropertyId, asset.IsArchived)).Single(x => x.Id == id));
        });
        api.MapGet("/assets/{id:guid}/history", async (Guid id, InventoryDbContext db) =>
        {
            if (!await db.Assets.AnyAsync(x => x.Id == id)) return Results.NotFound();
            var events = await db.AssetEvents.Where(x => x.AssetId == id).ToListAsync();
            return Results.Ok(events.OrderByDescending(x => x.OccurredOn).ThenByDescending(x => x.Kind == "Added" ? 0 : 1)
                .Select(x => new AssetEventDto(x.Id, x.AssetId, x.OccurredOn, x.Kind, x.Description, x.Cost)).ToList());
        });
        api.MapPost("/assets/{id:guid}/events", async (Guid id, AssetEventInput input, InventoryDbContext db) =>
        {
            if (!await db.Assets.AnyAsync(x => x.Id == id)) return Results.NotFound();
            // Added, Moved and (Un)archived are recorded by the app itself so the history can't contradict the asset.
            if (!AssetEvent.ManualKinds.Contains(input.Kind)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["kind"] = [$"Kind must be one of: {string.Join(", ", AssetEvent.ManualKinds)}."] });
            var entity = new AssetEvent { AssetId = id, OccurredOn = input.OccurredOn, Kind = input.Kind, Description = input.Description?.Trim(), Cost = input.Cost };
            db.AssetEvents.Add(entity);
            await db.SaveChangesAsync();
            return Results.Created($"/api/assets/{id}/history", new AssetEventDto(entity.Id, id, entity.OccurredOn, entity.Kind, entity.Description, entity.Cost));
        });
        api.MapDelete("/assets/{id:guid}/events/{eventId:guid}", async (Guid id, Guid eventId, InventoryDbContext db) =>
        {
            var entity = await db.AssetEvents.SingleOrDefaultAsync(x => x.Id == eventId && x.AssetId == id);
            if (entity is null) return Results.NotFound();
            if (!AssetEvent.ManualKinds.Contains(entity.Kind)) return Results.BadRequest("Only events entered by hand can be deleted.");
            db.AssetEvents.Remove(entity);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
        api.MapGet("/assets/{id:guid}/photos", async (Guid id, InventoryDbContext db) =>
        {
            if (!await db.Assets.AnyAsync(x => x.Id == id)) return Results.NotFound();
            return Results.Ok(await db.AssetPhotos.Where(x => x.AssetId == id).OrderBy(x => x.SortOrder).ThenBy(x => x.Id).Select(x => ToDto(x)).ToListAsync());
        });
        api.MapPost("/assets/{id:guid}/photos", async (Guid id, AssetPhotoInput input, InventoryDbContext db) =>
        {
            if (!await db.Assets.AnyAsync(x => x.Id == id)) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(input.StorageKey)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["storageKey"] = ["Storage key is required."] });
            var entity = new AssetPhoto { AssetId = id, StorageKey = input.StorageKey.Trim(), Caption = input.Caption?.Trim(), SortOrder = input.SortOrder };
            db.AssetPhotos.Add(entity);
            await db.SaveChangesAsync();
            return Results.Created($"/api/assets/{id}/photos/{entity.Id}", ToDto(entity));
        });
        api.MapDelete("/assets/{id:guid}/photos/{photoId:guid}", async (Guid id, Guid photoId, InventoryDbContext db, FileStore store) =>
        {
            var entity = await db.AssetPhotos.SingleOrDefaultAsync(x => x.Id == photoId && x.AssetId == id);
            if (entity is null) return Results.NotFound();
            db.AssetPhotos.Remove(entity);
            await db.SaveChangesAsync();
            await DeleteUnusedFiles(db, store, [entity.StorageKey]);
            return Results.NoContent();
        });

        api.MapGet("/maintenance", async (Guid? propertyId, Guid? fixtureId, InventoryDbContext db) =>
        {
            var tasks = db.MaintenanceTasks.AsQueryable();
            if (propertyId is not null) tasks = tasks.Where(x => x.PropertyId == propertyId.Value);
            if (fixtureId is not null) tasks = tasks.Where(x => x.FixtureId == fixtureId.Value);
            return Results.Ok(await MaintenanceDtos(db, tasks));
        });
        api.MapPost("/maintenance", async (MaintenanceTaskInput input, InventoryDbContext db) =>
        {
            var error = await ValidateMaintenance(input, db);
            if (error is not null) return error;
            var entity = new MaintenanceTask { Title = "" };
            ApplyMaintenance(input, entity);
            db.MaintenanceTasks.Add(entity);
            await db.SaveChangesAsync();
            return Results.Created($"/api/maintenance/{entity.Id}", (await MaintenanceDtos(db, db.MaintenanceTasks.Where(x => x.Id == entity.Id))).Single());
        });
        api.MapPut("/maintenance/{id:guid}", async (Guid id, MaintenanceTaskInput input, InventoryDbContext db) =>
        {
            var entity = await db.MaintenanceTasks.FindAsync(id);
            if (entity is null) return Results.NotFound();
            var error = await ValidateMaintenance(input, db);
            if (error is not null) return error;
            ApplyMaintenance(input, entity);
            await db.SaveChangesAsync();
            return Results.Ok((await MaintenanceDtos(db, db.MaintenanceTasks.Where(x => x.Id == id))).Single());
        });
        api.MapDelete("/maintenance/{id:guid}", async (Guid id, InventoryDbContext db) =>
        {
            var entity = await db.MaintenanceTasks.FindAsync(id);
            if (entity is null) return Results.NotFound();
            db.MaintenanceTasks.Remove(entity);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
        api.MapPost("/maintenance/{id:guid}/complete", async (Guid id, MaintenanceCompletionInput input, InventoryDbContext db) =>
        {
            var entity = await db.MaintenanceTasks.FindAsync(id);
            if (entity is null) return Results.NotFound();
            db.MaintenanceRecords.Add(new MaintenanceRecord { TaskId = id, CompletedOn = input.CompletedOn, Cost = input.Cost, Supplier = input.Supplier?.Trim(), Notes = input.Notes?.Trim() });
            // Recording an older service (back-filling history) must not move the schedule backwards.
            if (entity.LastCompletedOn is null || input.CompletedOn >= entity.LastCompletedOn)
            {
                entity.LastCompletedOn = input.CompletedOn;
                entity.DueOn = NextDue(input.CompletedOn, entity.IntervalValue, entity.IntervalUnit);
            }
            await db.SaveChangesAsync();
            return Results.Ok((await MaintenanceDtos(db, db.MaintenanceTasks.Where(x => x.Id == id))).Single());
        });
        api.MapGet("/maintenance/{id:guid}/history", async (Guid id, InventoryDbContext db) =>
        {
            if (!await db.MaintenanceTasks.AnyAsync(x => x.Id == id)) return Results.NotFound();
            return Results.Ok(await db.MaintenanceRecords.Where(x => x.TaskId == id).OrderByDescending(x => x.CompletedOn)
                .Select(x => new MaintenanceRecordDto(x.Id, x.TaskId, x.CompletedOn, x.Cost, x.Supplier, x.Notes)).ToListAsync());
        });
        api.MapPost("/files", async (IFormFile file, FileStore store) =>
        {
            await using var stream = file.OpenReadStream();
            var (key, error) = await store.SaveAsync(stream, file.Length);
            return key is null ? Results.BadRequest(error) : Results.Ok(new UploadedFileDto(key, System.IO.Path.GetFileName(file.FileName), FileStore.ContentTypeFor(key), file.Length));
        }).DisableAntiforgery(); // multipart uploads from the local WASM client; JSON endpoints don't use antiforgery either
        api.MapGet("/files/{**key}", (string key, FileStore store, HttpContext context) =>
        {
            var stream = store.Open(key);
            if (stream is null) return Results.NotFound();
            context.Response.Headers.XContentTypeOptions = "nosniff";
            return Results.File(stream, FileStore.ContentTypeFor(key), enableRangeProcessing: true);
        });

        api.MapGet("/documents", async (Guid? propertyId, Guid? roomId, Guid? fixtureId, Guid? assetId, Guid? maintenanceTaskId, string? kind, InventoryDbContext db) =>
        {
            var documents = db.Documents.AsQueryable();
            if (propertyId is not null) documents = documents.Where(x => x.PropertyId == propertyId);
            if (roomId is not null) documents = documents.Where(x => x.RoomId == roomId);
            if (fixtureId is not null) documents = documents.Where(x => x.FixtureId == fixtureId);
            if (assetId is not null) documents = documents.Where(x => x.AssetId == assetId);
            if (maintenanceTaskId is not null) documents = documents.Where(x => x.MaintenanceTaskId == maintenanceTaskId);
            if (!string.IsNullOrWhiteSpace(kind)) documents = documents.Where(x => x.Kind == kind);
            return Results.Ok(await DocumentDtos(db, documents));
        });
        api.MapPost("/documents", async (DocumentInput input, InventoryDbContext db) =>
        {
            var error = await ValidateDocument(input, db);
            if (error is not null) return error;
            var entity = new Document { Title = "", Kind = "", StorageKey = "" };
            ApplyDocument(input, entity);
            db.Documents.Add(entity);
            await db.SaveChangesAsync();
            return Results.Created($"/api/documents/{entity.Id}", (await DocumentDtos(db, db.Documents.Where(x => x.Id == entity.Id))).Single());
        });
        api.MapPut("/documents/{id:guid}", async (Guid id, DocumentInput input, InventoryDbContext db, FileStore store) =>
        {
            var entity = await db.Documents.FindAsync(id);
            if (entity is null) return Results.NotFound();
            var error = await ValidateDocument(input, db);
            if (error is not null) return error;
            var oldKey = entity.StorageKey;
            ApplyDocument(input, entity);
            await db.SaveChangesAsync();
            if (oldKey != entity.StorageKey) await DeleteUnusedFiles(db, store, [oldKey]);
            return Results.Ok((await DocumentDtos(db, db.Documents.Where(x => x.Id == id))).Single());
        });
        api.MapDelete("/documents/{id:guid}", async (Guid id, InventoryDbContext db, FileStore store) =>
        {
            var entity = await db.Documents.FindAsync(id);
            if (entity is null) return Results.NotFound();
            db.Documents.Remove(entity);
            await db.SaveChangesAsync();
            await DeleteUnusedFiles(db, store, [entity.StorageKey]);
            return Results.NoContent();
        });
        // Insurance schedule: active assets and fixtures per property, valued in that property's currency.
        api.MapGet("/reports/insurance", async (Guid? propertyId, InventoryDbContext db) =>
        {
            var properties = await db.Properties.Where(x => propertyId == null || x.Id == propertyId).OrderBy(x => x.Name).ToListAsync();
            if (propertyId is not null && properties.Count == 0) return Results.NotFound();
            var ids = properties.Select(x => x.Id).ToList();
            var assets = await AssetDtos(db, propertyId, false);
            var fixtures = await FixtureDtos(db, db.Fixtures.Where(x => x.Room != null && ids.Contains(x.Room.PropertyId)));
            var fixtureProperties = await db.Fixtures.Where(x => x.Room != null && ids.Contains(x.Room.PropertyId)).Select(x => new { x.Id, x.Room!.PropertyId }).ToDictionaryAsync(x => x.Id, x => x.PropertyId);
            var assetPhotos = await db.AssetPhotos.GroupBy(x => x.AssetId).Select(x => new { x.Key, Count = x.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count);
            var fixturePhotos = await db.FixturePhotos.GroupBy(x => x.FixtureId).Select(x => new { x.Key, Count = x.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count);
            var receipts = await db.Documents.Where(x => x.Kind == "Receipt" || x.Kind == "Invoice").Select(x => new { x.AssetId, x.FixtureId }).ToListAsync();
            var assetReceipts = receipts.Where(x => x.AssetId != null).Select(x => x.AssetId!.Value).ToHashSet();
            var fixtureReceipts = receipts.Where(x => x.FixtureId != null).Select(x => x.FixtureId!.Value).ToHashSet();
            static string? BrandModel(string? brand, string? model) => string.IsNullOrWhiteSpace($"{brand}{model}") ? null : $"{brand} {model}".Trim();

            var report = properties.Select(property =>
            {
                var items = assets.Where(x => x.PropertyId == property.Id)
                    .Select(x => new InsuranceItemDto("Asset", x.Name, x.Category, BrandModel(x.Brand, x.Model), x.SerialNumber, x.PurchaseDate, x.PurchasePrice, x.CurrentValue, x.LocationPath, assetPhotos.GetValueOrDefault(x.Id), assetReceipts.Contains(x.Id)))
                    .Concat(fixtures.Where(x => fixtureProperties.GetValueOrDefault(x.Id) == property.Id)
                        .Select(x => new InsuranceItemDto("Fixture", x.Name, x.Type, BrandModel(x.Manufacturer, x.Model), x.SerialNumber, x.PurchaseDate, x.PurchasePrice, x.CurrentValue, x.LocationPath, fixturePhotos.GetValueOrDefault(x.Id), fixtureReceipts.Contains(x.Id))))
                    .OrderBy(x => x.Kind).ThenBy(x => x.Category).ThenByDescending(x => x.CurrentValue).ToList();
                var categories = items.GroupBy(x => x.Category).Select(x => new CategoryTotalDto(x.Key, x.Sum(y => y.CurrentValue ?? 0))).OrderByDescending(x => x.Total).ToList();
                return new InsurancePropertyDto(property.Id, property.Name, property.Address, NormalizeCurrency(property.Currency) ?? "USD",
                    items.Where(x => x.Kind == "Asset").Sum(x => x.CurrentValue ?? 0), items.Where(x => x.Kind == "Fixture").Sum(x => x.CurrentValue ?? 0), categories, items);
            }).ToList();
            return Results.Ok(new InsuranceReportDto(Today(), report));
        });
        // QR code as SVG for printable labels (the text is normally a link to the item's scan page).
        api.MapGet("/qr", (string? text) =>
        {
            if (string.IsNullOrWhiteSpace(text) || text.Length > 512) return Results.BadRequest("Text is required and must be 512 characters or fewer.");
            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
            return Results.Content(new SvgQRCode(data).GetGraphic(4), "image/svg+xml");
        });
        api.MapGet("/dashboard", async (InventoryDbContext db) =>
        {
            // TODO: Include recent purchases and room completion metrics.
            // Values are only summed within one currency; properties with different currencies get separate totals.
            var assets = await db.Assets.Where(x => !x.IsArchived).Select(x => new { x.Category, Value = x.CurrentValue ?? 0, Currency = x.Property!.Currency }).ToListAsync();
            var fixtures = await db.Fixtures.Select(x => new { Category = x.Type, Value = x.CurrentValue ?? 0, Currency = x.Room!.Floor!.Property!.Currency }).ToListAsync();
            var totals = assets.Select(x => (x.Currency, x.Category, x.Value, IsFixture: false))
                .Concat(fixtures.Select(x => (x.Currency, x.Category, x.Value, IsFixture: true)))
                .GroupBy(x => NormalizeCurrency(x.Currency) ?? "USD")
                .OrderByDescending(x => x.Sum(y => y.Value))
                .Select(x => new CurrencyTotalDto(
                    x.Key,
                    x.Sum(y => y.Value),
                    x.Where(y => y.IsFixture).Sum(y => y.Value),
                    x.GroupBy(y => y.Category).Select(y => new CategoryTotalDto(y.Key, y.Sum(z => z.Value))).OrderByDescending(y => y.Total).ToList()))
                .ToList();
            var today = Today();
            var dueDates = await db.MaintenanceTasks.Where(x => x.DueOn != null).Select(x => x.DueOn!.Value).ToListAsync();
            var warrantiesExpiring = await db.Documents.CountAsync(x => x.Kind == "Warranty" && x.ExpiresOn >= today && x.ExpiresOn <= today.AddDays(90));
            var missingReceipts = await db.Assets.CountAsync(x => !x.IsArchived && !db.Documents.Any(d => d.AssetId == x.Id && (d.Kind == "Receipt" || d.Kind == "Invoice")));
            return new DashboardDto(assets.Count, fixtures.Count, totals, dueDates.Count(x => x < today), dueDates.Count(x => x >= today && x <= today.AddDays(30)), warrantiesExpiring, missingReceipts);
        });
        api.MapGet("/search", async (string? q, InventoryDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(q)) return Results.Ok(Array.Empty<SearchResultDto>());
            var term = q.Trim();
            // SQLite LIKE is case-insensitive for ASCII; % and _ in the search text are matched literally.
            var pattern = $"%{term.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal)}%";
            const int Limit = 50;

            var assets = await AssetDtos(db, db.Assets.Where(x => !x.IsArchived && (EF.Functions.Like(x.Name, pattern, "\\") || EF.Functions.Like(x.Category, pattern, "\\")
                || EF.Functions.Like(x.Brand, pattern, "\\") || EF.Functions.Like(x.Model, pattern, "\\") || EF.Functions.Like(x.SerialNumber, pattern, "\\") || EF.Functions.Like(x.Notes, pattern, "\\"))).Take(Limit));
            var fixtures = await FixtureDtos(db, db.Fixtures.Where(x => EF.Functions.Like(x.Name, pattern, "\\") || EF.Functions.Like(x.Type, pattern, "\\") || EF.Functions.Like(x.Manufacturer, pattern, "\\")
                || EF.Functions.Like(x.Model, pattern, "\\") || EF.Functions.Like(x.SerialNumber, pattern, "\\") || EF.Functions.Like(x.Notes, pattern, "\\") || EF.Functions.Like(x.Provider, pattern, "\\")).Take(Limit));
            // Location paths are computed, so locations (a small table) are matched in memory.
            var locations = (await LocationDtos(db, null)).Where(x => $"{x.Name} {x.Type} {x.Path}".Contains(term, StringComparison.OrdinalIgnoreCase)).Take(Limit).ToList();
            var paints = await db.Paints.Where(x => EF.Functions.Like(x.Brand, pattern, "\\") || EF.Functions.Like(x.ColorName, pattern, "\\") || EF.Functions.Like(x.ColorCode, pattern, "\\")
                || EF.Functions.Like(x.Finish, pattern, "\\") || EF.Functions.Like(x.Notes, pattern, "\\")).Take(Limit).ToListAsync();
            var surfaces = await db.Surfaces.Where(x => EF.Functions.Like(x.Name, pattern, "\\") || EF.Functions.Like(x.SurfaceType, pattern, "\\") || EF.Functions.Like(x.PaintBrand, pattern, "\\")
                || EF.Functions.Like(x.ColorName, pattern, "\\") || EF.Functions.Like(x.ColorCode, pattern, "\\") || EF.Functions.Like(x.Finish, pattern, "\\") || EF.Functions.Like(x.Material, pattern, "\\")
                || EF.Functions.Like(x.Manufacturer, pattern, "\\") || EF.Functions.Like(x.ProductName, pattern, "\\") || EF.Functions.Like(x.Notes, pattern, "\\")).Take(Limit).ToListAsync();
            var documents = await DocumentDtos(db, db.Documents.Where(x => EF.Functions.Like(x.Title, pattern, "\\") || EF.Functions.Like(x.Kind, pattern, "\\") || EF.Functions.Like(x.Tags, pattern, "\\")
                || EF.Functions.Like(x.Notes, pattern, "\\") || EF.Functions.Like(x.FileName, pattern, "\\")).Take(Limit));

            var roomPaths = surfaces.Count > 0 ? await RoomPaths(db) : [];
            var paintResults = new List<SearchResultDto>();
            foreach (var paint in paints)
            {
                var usedIn = (await PaintUsage(db, paint)).Select(x => x.RoomPath).Distinct().ToList();
                paintResults.Add(new SearchResultDto("Paint", paint.Id, $"{paint.Brand} {paint.ColorName}", string.Join(" ", new[] { paint.ColorCode, paint.Finish }.Where(x => !string.IsNullOrWhiteSpace(x))), usedIn.Count == 0 ? null : string.Join(", ", usedIn)));
            }
            var results = assets.Select(x => new SearchResultDto("Asset", x.Id, x.Name, x.Category, x.LocationPath))
                .Concat(fixtures.Select(x => new SearchResultDto("Fixture", x.Id, x.Name, x.Type, x.LocationPath)))
                .Concat(locations.Select(x => new SearchResultDto("Storage", x.Id, x.Name, x.Type ?? "Storage location", x.Path)))
                .Concat(paintResults)
                .Concat(surfaces.Select(x => new SearchResultDto("Surface", x.Id, x.Name, string.Join(" · ", new[] { x.SurfaceType, x.SurfaceType == "flooring" ? x.Material : x.PaintBrand, x.ColorName }.Where(y => !string.IsNullOrWhiteSpace(y))), roomPaths.GetValueOrDefault(x.RoomId))))
                .Concat(documents.Select(x => new SearchResultDto("Document", x.Id, x.Title, x.Kind, x.AttachedTo ?? x.PropertyName)))
                .Take(Limit);
            return Results.Ok(results);
        });

        api.MapGet("/export", async (InventoryDbContext db) => Results.Ok(await Export(db)));
        // Full backup: inventory.json plus every uploaded file it references, so photos and documents survive a restore.
        api.MapGet("/backup", async (InventoryDbContext db, FileStore store) =>
        {
            var inventory = await Export(db);
            var keys = await StoredFileKeys(db);
            var temp = new FileStream(System.IO.Path.GetTempFileName(), FileMode.Create, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
            using (var zip = new ZipArchive(temp, ZipArchiveMode.Create, leaveOpen: true))
            {
                await using (var json = zip.CreateEntry("inventory.json", CompressionLevel.Optimal).Open())
                    await JsonSerializer.SerializeAsync(json, inventory, BackupJson);
                foreach (var key in keys)
                {
                    await using var source = store.Open(key);
                    if (source is null) continue; // referenced but missing on disk: the metadata is still backed up
                    await using var entry = zip.CreateEntry($"files/{key}", CompressionLevel.NoCompression).Open(); // images and PDFs are already compressed
                    await source.CopyToAsync(entry);
                }
            }
            temp.Position = 0;
            return Results.File(temp, "application/zip", $"home-inventory-backup-{DateTime.Now:yyyy-MM-dd}.zip");
        });
        // Restores the files from a backup ZIP and returns its inventory for the usual preview/confirm steps.
        api.MapPost("/import/zip", async (HttpContext context, FileStore store) =>
        {
            var sizeLimit = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (sizeLimit is { IsReadOnly: false }) sizeLimit.MaxRequestBodySize = MaxBackupBytes;
            await using var temp = new FileStream(System.IO.Path.GetTempFileName(), FileMode.Create, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
            await context.Request.Body.CopyToAsync(temp);
            if (temp.Length > MaxBackupBytes) return Results.BadRequest("Backups must be 1 GB or smaller.");
            temp.Position = 0;
            ZipArchive zip;
            try { zip = new ZipArchive(temp, ZipArchiveMode.Read); }
            catch (InvalidDataException) { return Results.BadRequest("That file is not a valid backup ZIP."); }
            using (zip)
            {
                var jsonEntry = zip.GetEntry("inventory.json");
                if (jsonEntry is null || jsonEntry.Length > 200 * 1024 * 1024) return Results.BadRequest("The backup ZIP has no inventory.json.");
                InventoryExport? inventory;
                try
                {
                    await using var json = jsonEntry.Open();
                    inventory = await JsonSerializer.DeserializeAsync<InventoryExport>(json, BackupJson);
                }
                catch (JsonException) { return Results.BadRequest("The backup's inventory.json is not valid."); }
                if (inventory is null) return Results.BadRequest("The backup's inventory.json is empty.");
                int restored = 0, skipped = 0;
                // Only entries named files/<valid key> are considered, so crafted paths can't escape the files folder.
                foreach (var entry in zip.Entries.Where(x => x.FullName.StartsWith("files/", StringComparison.Ordinal)))
                {
                    await using var content = entry.Open();
                    if (await store.RestoreAsync(entry.FullName["files/".Length..], content, entry.Length)) restored++; else skipped++;
                }
                return Results.Ok(new ZipImportDto(inventory, restored, skipped));
            }
        });
        api.MapPost("/import/preview", async (InventoryExport import, InventoryDbContext db) => Results.Ok(await Preview(import, db)));
        api.MapPost("/import/confirm", async (ImportConfirmation confirmation, InventoryDbContext db) =>
        {
            var preview = await Preview(confirmation.Inventory, db);
            if (!preview.IsValid) return Results.BadRequest(preview);
            var inventory = confirmation.Inventory;
            await using var transaction = await db.Database.BeginTransactionAsync();
            // Each map starts with the rows that already exist (by backup ID), so re-importing a backup reuses them and only adds what's new.
            var properties = await ExistingIds(db.Properties);
            var floors = await ExistingIds(db.Floors);
            var rooms = await ExistingIds(db.Rooms);
            var locations = await ExistingIds(db.StorageLocations);
            var assets = await ExistingIds(db.Assets);
            var fixtures = await ExistingIds(db.Fixtures);
            var paints = await ExistingIds(db.Paints);
            var propertyPhotos = await ExistingIds(db.PropertyPhotos);
            var roomPhotos = await ExistingIds(db.RoomPhotos);
            var fixturePhotos = await ExistingIds(db.FixturePhotos);
            var assetPhotos = await ExistingIds(db.AssetPhotos);
            var surfaces = await ExistingIds(db.Surfaces);
            var tasks = await ExistingIds(db.MaintenanceTasks);
            var records = await ExistingIds(db.MaintenanceRecords);
            var documents = await ExistingIds(db.Documents);
            var assetEvents = await ExistingIds(db.AssetEvents);

            foreach (var property in inventory.Properties.Where(x => !properties.ContainsKey(x.ExternalId)))
            {
                var entity = new Property { ExternalId = property.ExternalId, Name = property.Name, Address = property.Address, PurchaseDate = property.PurchaseDate, PurchasePrice = property.PurchasePrice, FloorArea = property.FloorArea, Currency = NormalizeCurrency(property.Currency) ?? "USD", Notes = property.Notes };
                db.Properties.Add(entity);
                properties[property.ExternalId] = entity.Id;
            }
            foreach (var photo in inventory.PropertyPhotos.Where(x => !propertyPhotos.ContainsKey(x.ExternalId)))
                db.PropertyPhotos.Add(new PropertyPhoto { ExternalId = photo.ExternalId, PropertyId = properties[photo.PropertyExternalId], StorageKey = photo.StorageKey?.Trim() ?? string.Empty, Caption = photo.Caption?.Trim(), SortOrder = photo.SortOrder });

            var floorProperties = await db.Floors.ToDictionaryAsync(x => x.Id, x => x.PropertyId);
            foreach (var floor in inventory.Floors.Where(x => !floors.ContainsKey(x.ExternalId)))
            {
                var entity = new Floor { ExternalId = floor.ExternalId, PropertyId = properties[floor.PropertyExternalId], Name = floor.Name, Notes = floor.Notes };
                db.Floors.Add(entity);
                floors[floor.ExternalId] = entity.Id;
                floorProperties[entity.Id] = entity.PropertyId;
            }
            foreach (var room in inventory.Rooms.Where(x => !rooms.ContainsKey(x.ExternalId)))
            {
                var floorId = floors[room.FloorExternalId];
                var entity = new Room
                {
                    ExternalId = room.ExternalId,
                    PropertyId = floorProperties[floorId],
                    FloorId = floorId,
                    Name = room.Name,
                    Type = room.Type,
                    Area = room.Area,
                    Volume = room.Volume,
                    CeilingHeight = room.CeilingHeight,
                    Length = room.Length,
                    Width = room.Width,
                    Height = room.Height,
                    Flooring = room.Flooring,
                    WallFinish = room.WallFinish,
                    CeilingFinish = room.CeilingFinish,
                    PaintDetails = room.PaintDetails,
                    WindowsCount = room.WindowsCount,
                    DoorsCount = room.DoorsCount,
                    FixturesNotes = room.FixturesNotes,
                    UtilitiesNotes = room.UtilitiesNotes,
                    Notes = room.Notes
                };
                db.Rooms.Add(entity);
                rooms[room.ExternalId] = entity.Id;
            }
            foreach (var surface in inventory.Surfaces.Where(x => !surfaces.ContainsKey(x.ExternalId)))
                db.Surfaces.Add(new Surface { ExternalId = surface.ExternalId, RoomId = rooms[surface.RoomExternalId], Name = surface.Name, SurfaceType = surface.SurfaceType, PaintBrand = surface.PaintBrand, ColorName = surface.ColorName, ColorCode = surface.ColorCode, Finish = surface.Finish, Coats = surface.Coats, PaintedDate = surface.PaintedDate, Painter = surface.Painter, QuantityPurchased = surface.QuantityPurchased, Manufacturer = surface.Manufacturer, ProductName = surface.ProductName, Material = surface.Material, Supplier = surface.Supplier, Warranty = surface.Warranty, Invoice = surface.Invoice, InstallationDate = surface.InstallationDate, Notes = surface.Notes, SortOrder = surface.SortOrder });

            // Files may list a child location before its parent, so add locations parent-first.
            var pendingLocations = inventory.StorageLocations.Where(x => !locations.ContainsKey(x.ExternalId)).ToList();
            while (pendingLocations.Count > 0)
            {
                var ready = pendingLocations.Where(x => x.ParentExternalId is null || locations.ContainsKey(x.ParentExternalId)).ToList();
                foreach (var location in ready)
                {
                    var entity = new StorageLocation { ExternalId = location.ExternalId, PropertyId = properties[location.PropertyExternalId], ParentId = location.ParentExternalId is null ? null : locations[location.ParentExternalId], Name = location.Name, Type = location.Type };
                    db.StorageLocations.Add(entity);
                    locations[location.ExternalId] = entity.Id;
                }
                if (ready.Count == 0) return Results.BadRequest("Storage locations reference a missing parent or form a cycle.");
                pendingLocations = pendingLocations.Except(ready).ToList();
            }

            foreach (var fixture in (inventory.Fixtures ?? []).Where(x => !fixtures.ContainsKey(x.ExternalId)))
            {
                var entity = new Fixture { ExternalId = fixture.ExternalId, RoomId = rooms[fixture.RoomExternalId], Name = fixture.Name, Type = fixture.Type, Manufacturer = fixture.Manufacturer, Model = fixture.Model, SerialNumber = fixture.SerialNumber, PurchaseDate = fixture.PurchaseDate, PurchasePrice = fixture.PurchasePrice, CurrentValue = fixture.CurrentValue, Warranty = fixture.Warranty, ManualUrl = fixture.ManualUrl, InstallerName = fixture.InstallerName, InstallationDate = fixture.InstallationDate, MaintenanceSchedule = fixture.MaintenanceSchedule, LastMaintenanceDate = fixture.LastMaintenanceDate, Condition = fixture.Condition, Notes = fixture.Notes, Category = FixtureCategory(fixture.Category), Provider = fixture.Provider, AccountNumber = fixture.AccountNumber };
                db.Fixtures.Add(entity);
                fixtures[fixture.ExternalId] = entity.Id;
            }
            foreach (var photo in (inventory.FixturePhotos ?? []).Where(x => !fixturePhotos.ContainsKey(x.ExternalId)))
                db.FixturePhotos.Add(new FixturePhoto { ExternalId = photo.ExternalId, FixtureId = fixtures[photo.FixtureExternalId], StorageKey = photo.StorageKey?.Trim() ?? string.Empty, Caption = photo.Caption?.Trim(), SortOrder = photo.SortOrder });
            foreach (var photo in (inventory.RoomPhotos ?? []).Where(x => !roomPhotos.ContainsKey(x.ExternalId)))
                db.RoomPhotos.Add(new RoomPhoto { ExternalId = photo.ExternalId, RoomId = rooms[photo.RoomExternalId], StorageKey = photo.StorageKey?.Trim() ?? string.Empty, Caption = photo.Caption?.Trim(), SortOrder = photo.SortOrder });

            // The paint library is global, so also reuse an existing paint with the same brand, colour name and code instead of duplicating it.
            var existingPaints = await db.Paints.ToListAsync();
            foreach (var paint in (inventory.Paints ?? []).Where(x => !paints.ContainsKey(x.ExternalId)))
            {
                var match = existingPaints.FirstOrDefault(x => Norm(x.Brand) == Norm(paint.Brand) && Norm(x.ColorName) == Norm(paint.ColorName) && Norm(x.ColorCode) == Norm(paint.ColorCode));
                if (match is null)
                {
                    match = new Paint { ExternalId = paint.ExternalId, Brand = paint.Brand.Trim(), ColorName = paint.ColorName.Trim(), ColorCode = paint.ColorCode?.Trim(), Finish = paint.Finish?.Trim(), Notes = paint.Notes?.Trim() };
                    db.Paints.Add(match);
                    existingPaints.Add(match);
                }
                paints[paint.ExternalId] = match.Id;
            }
            var existingRoomPaints = (await db.RoomPaints.Select(x => new { x.RoomId, x.PaintId }).ToListAsync()).Select(x => (x.RoomId, x.PaintId)).ToHashSet();
            foreach (var roomPaint in inventory.RoomPaints ?? [])
            {
                var key = (rooms[roomPaint.RoomExternalId], paints[roomPaint.PaintExternalId]);
                if (existingRoomPaints.Add(key)) db.RoomPaints.Add(new RoomPaint { RoomId = key.Item1, PaintId = key.Item2, SortOrder = roomPaint.SortOrder, Surface = roomPaint.Surface?.Trim() });
            }

            foreach (var asset in inventory.Assets.Where(x => !assets.ContainsKey(x.ExternalId) && !confirmation.SkipExternalIds.Contains(x.ExternalId)))
            {
                var entity = new Asset { ExternalId = asset.ExternalId, PropertyId = properties[asset.PropertyExternalId], RoomId = asset.RoomExternalId is null ? null : rooms[asset.RoomExternalId], StorageLocationId = asset.StorageLocationExternalId is null ? null : locations[asset.StorageLocationExternalId], Name = asset.Name, Category = asset.Category, Description = asset.Description, Brand = asset.Brand, Model = asset.Model, SerialNumber = asset.SerialNumber, PurchaseDate = asset.PurchaseDate, PurchasePrice = asset.PurchasePrice, CurrentValue = asset.CurrentValue, Condition = asset.Condition, Notes = asset.Notes };
                db.Assets.Add(entity);
                assets[asset.ExternalId] = entity.Id;
            }
            foreach (var photo in (inventory.AssetPhotos ?? []).Where(x => !assetPhotos.ContainsKey(x.ExternalId) && assets.ContainsKey(x.AssetExternalId)))
                db.AssetPhotos.Add(new AssetPhoto { ExternalId = photo.ExternalId, AssetId = assets[photo.AssetExternalId], StorageKey = photo.StorageKey?.Trim() ?? string.Empty, Caption = photo.Caption?.Trim(), SortOrder = photo.SortOrder });

            foreach (var task in (inventory.MaintenanceTasks ?? []).Where(x => !tasks.ContainsKey(x.ExternalId)))
            {
                var recurring = task.IntervalValue is > 0;
                var entity = new MaintenanceTask { ExternalId = task.ExternalId, PropertyId = properties[task.PropertyExternalId], FixtureId = task.FixtureExternalId is null ? null : fixtures[task.FixtureExternalId], Title = task.Title.Trim(), IntervalValue = recurring ? task.IntervalValue : null, IntervalUnit = recurring ? task.IntervalUnit?.ToLowerInvariant() : null, DueOn = task.DueOn, LastCompletedOn = task.LastCompletedOn, Supplier = task.Supplier?.Trim(), EstimatedCost = task.EstimatedCost, Notes = task.Notes?.Trim() };
                db.MaintenanceTasks.Add(entity);
                tasks[task.ExternalId] = entity.Id;
            }
            foreach (var record in (inventory.MaintenanceRecords ?? []).Where(x => !records.ContainsKey(x.ExternalId)))
                db.MaintenanceRecords.Add(new MaintenanceRecord { ExternalId = record.ExternalId, TaskId = tasks[record.TaskExternalId], CompletedOn = record.CompletedOn, Cost = record.Cost, Supplier = record.Supplier?.Trim(), Notes = record.Notes?.Trim() });
            foreach (var document in (inventory.Documents ?? []).Where(x => !documents.ContainsKey(x.ExternalId)))
                db.Documents.Add(new Document
                {
                    ExternalId = document.ExternalId,
                    PropertyId = properties[document.PropertyExternalId],
                    RoomId = document.RoomExternalId is null ? null : rooms[document.RoomExternalId],
                    FixtureId = document.FixtureExternalId is null ? null : fixtures[document.FixtureExternalId],
                    // A skipped duplicate asset still maps to the existing asset; an asset skipped without a match leaves the document at property level.
                    AssetId = document.AssetExternalId is not null && assets.TryGetValue(document.AssetExternalId, out var assetId) ? assetId : null,
                    MaintenanceTaskId = document.MaintenanceTaskExternalId is null ? null : tasks[document.MaintenanceTaskExternalId],
                    Title = document.Title.Trim(), Kind = document.Kind, StorageKey = document.StorageKey.Trim(), FileName = document.FileName, ContentType = document.ContentType, SizeBytes = document.SizeBytes,
                    DocumentDate = document.DocumentDate, ExpiresOn = document.ExpiresOn, Tags = document.Tags?.Trim(), Notes = document.Notes?.Trim()
                });

            // Skipped duplicate assets map to the existing asset, so their history is merged rather than lost.
            foreach (var assetEvent in (inventory.AssetEvents ?? []).Where(x => !assetEvents.ContainsKey(x.ExternalId) && assets.ContainsKey(x.AssetExternalId)))
                db.AssetEvents.Add(new AssetEvent { ExternalId = assetEvent.ExternalId, AssetId = assets[assetEvent.AssetExternalId], OccurredOn = assetEvent.OccurredOn, Kind = assetEvent.Kind, Description = assetEvent.Description?.Trim(), Cost = assetEvent.Cost });

            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return Results.NoContent();
        });
        return api;
    }

    const long MaxBackupBytes = 1L << 30;
    static readonly JsonSerializerOptions BackupJson = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    static async Task<List<string>> StoredFileKeys(InventoryDbContext db)
    {
        var keys = await db.Documents.Select(x => x.StorageKey).ToListAsync();
        keys.AddRange(await db.PropertyPhotos.Select(x => x.StorageKey).ToListAsync());
        keys.AddRange(await db.RoomPhotos.Select(x => x.StorageKey).ToListAsync());
        keys.AddRange(await db.FixturePhotos.Select(x => x.StorageKey).ToListAsync());
        keys.AddRange(await db.AssetPhotos.Select(x => x.StorageKey).ToListAsync());
        return keys.Where(FileStore.IsStoredKey).Distinct().ToList();
    }
    public sealed record ImportConfirmation(InventoryExport Inventory, List<string> SkipExternalIds);
    static PropertyDto ToDto(Property x) => new(x.Id, x.Name, x.Address, x.PurchaseDate, x.PurchasePrice, x.FloorArea, x.Notes, x.Currency);
    static FixtureDto ToDto(Fixture x, string? locationPath) => new(x.Id, x.RoomId, x.Name, x.Type, x.Manufacturer, x.Model, x.SerialNumber, x.PurchaseDate, x.PurchasePrice, x.CurrentValue, x.Warranty, x.ManualUrl, x.InstallerName, x.InstallationDate, x.MaintenanceSchedule, x.LastMaintenanceDate, x.Condition, x.Notes, locationPath, x.Category, x.Provider, x.AccountNumber);
    static string FixtureCategory(string? value) => string.Equals(value?.Trim(), "Utility", StringComparison.OrdinalIgnoreCase) ? "Utility" : "Fixture";
    static FloorDto ToDto(Floor x) => new(x.Id, x.PropertyId, x.Name, x.Notes);
    static RoomDto ToDto(Room x) => new(x.Id, x.FloorId ?? Guid.Empty, x.Name, x.Type, x.Area, x.Volume, x.CeilingHeight, x.Length, x.Width, x.Height, x.Flooring, x.WallFinish, x.CeilingFinish, x.PaintDetails, x.WindowsCount, x.DoorsCount, x.FixturesNotes, x.UtilitiesNotes, x.Notes);
    static SurfaceDto ToDto(Surface x) => new(x.Id, x.RoomId, x.Name, x.SurfaceType, x.PaintBrand, x.ColorName, x.ColorCode, x.Finish, x.Coats, x.PaintedDate, x.Painter, x.QuantityPurchased, x.Manufacturer, x.ProductName, x.Material, x.Supplier, x.Warranty, x.Invoice, x.InstallationDate, x.Notes, x.SortOrder);
    static PaintDto ToDto(Paint x) => new(x.Id, x.Brand, x.ColorName, x.ColorCode, x.Finish, x.Notes);
    static PhotoMetadataDto ToDto(PropertyPhoto x) => new(x.Id, x.StorageKey, x.Caption, x.SortOrder);
    static PhotoMetadataDto ToDto(RoomPhoto x) => new(x.Id, x.StorageKey, x.Caption, x.SortOrder);
    static FixturePhotoDto ToDto(FixturePhoto x) => new(x.Id, x.StorageKey, x.Caption, x.SortOrder);
    static AssetPhotoDto ToDto(AssetPhoto x) => new(x.Id, x.StorageKey, x.Caption, x.SortOrder);
    static async Task<List<RoomPaintDto>> RoomPaintDtos(InventoryDbContext db, Guid roomId) =>
        await db.RoomPaints
            .Where(x => x.RoomId == roomId)
            .Join(db.Paints, rp => rp.PaintId, p => p.Id, (rp, p) => new { rp.SortOrder, rp.Surface, p.Id, p.Brand, p.ColorName, p.ColorCode, p.Finish, p.Notes })
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Brand)
            .ThenBy(x => x.ColorName)
            .Select(x => new RoomPaintDto(x.Id, x.Brand, x.ColorName, x.ColorCode, x.Finish, x.Notes, x.SortOrder, x.Surface))
            .ToListAsync();
    static async Task<List<StorageLocationDto>> LocationDtos(InventoryDbContext db, Guid? propertyId)
    {
        var list = await db.StorageLocations.Where(x => propertyId == null || x.PropertyId == propertyId).ToListAsync();
        var byId = list.ToDictionary(x => x.Id);
        return list.Select(x => new StorageLocationDto(x.Id, x.PropertyId, x.ParentId, x.Name, x.Type, Path(x, byId))).OrderBy(x => x.Path).ToList();
    }
    static readonly string[] IntervalUnits = ["days", "months", "years"];
    static DateOnly Today() => DateOnly.FromDateTime(DateTime.Today);
    /// <summary>Next due date after a completion; null for one-off tasks, which are finished once done.</summary>
    static DateOnly? NextDue(DateOnly from, int? value, string? unit) => (value, unit) switch
    {
        (int v, "days") => from.AddDays(v),
        (int v, "months") => from.AddMonths(v),
        (int v, "years") => from.AddYears(v),
        _ => null
    };
    static async Task<IResult?> ValidateMaintenance(MaintenanceTaskInput input, InventoryDbContext db)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(input.Title)) errors["title"] = ["Title is required."];
        var unit = input.IntervalUnit?.Trim().ToLowerInvariant();
        if (input.IntervalValue is not null || !string.IsNullOrWhiteSpace(unit))
        {
            if (input.IntervalValue is not > 0) errors["intervalValue"] = ["Repeat interval must be a positive number."];
            if (!IntervalUnits.Contains(unit)) errors["intervalUnit"] = ["Repeat unit must be days, months or years."];
        }
        if (errors.Count > 0) return Results.ValidationProblem(errors);
        if (!await db.Properties.AnyAsync(x => x.Id == input.PropertyId)) return Results.BadRequest("The selected property does not exist.");
        if (input.FixtureId is not null && !await db.Fixtures.AnyAsync(x => x.Id == input.FixtureId && x.Room != null && x.Room.PropertyId == input.PropertyId))
            return Results.BadRequest("The fixture must be in the selected property.");
        return null;
    }
    static void ApplyMaintenance(MaintenanceTaskInput x, MaintenanceTask t)
    {
        var recurring = x.IntervalValue is > 0;
        t.PropertyId = x.PropertyId;
        t.FixtureId = x.FixtureId;
        t.Title = x.Title.Trim();
        t.IntervalValue = recurring ? x.IntervalValue : null;
        t.IntervalUnit = recurring ? x.IntervalUnit?.Trim().ToLowerInvariant() : null;
        t.DueOn = x.DueOn;
        t.Supplier = x.Supplier?.Trim();
        t.EstimatedCost = x.EstimatedCost;
        t.Notes = x.Notes?.Trim();
    }
    static async Task<List<MaintenanceTaskDto>> MaintenanceDtos(InventoryDbContext db, IQueryable<MaintenanceTask> tasks)
    {
        var list = await tasks.Include(x => x.Property).Include(x => x.Fixture).ToListAsync();
        // Overdue and soonest-due first; tasks without a due date (finished one-offs or unscheduled) last.
        return list.OrderBy(x => x.DueOn is null).ThenBy(x => x.DueOn).ThenBy(x => x.Title)
            .Select(x => new MaintenanceTaskDto(x.Id, x.PropertyId, x.FixtureId, x.Title, x.IntervalValue, x.IntervalUnit, x.DueOn, x.LastCompletedOn, x.Supplier, x.EstimatedCost, x.Notes, x.Property?.Name ?? "", x.Fixture?.Name, NormalizeCurrency(x.Property?.Currency) ?? "USD"))
            .ToList();
    }
    // Uploaded files referenced by photos and documents that a cascade delete of this property, floor, room or fixture removes.
    static async Task<List<string>> FileKeysFor(InventoryDbContext db, Guid? propertyId = null, Guid? floorId = null, Guid? roomId = null, Guid? fixtureId = null)
    {
        var rooms = db.Rooms.Where(x => propertyId != null && x.PropertyId == propertyId || floorId != null && x.FloorId == floorId || roomId != null && x.Id == roomId).Select(x => x.Id);
        var fixtures = db.Fixtures.Where(x => rooms.Contains(x.RoomId) || fixtureId != null && x.Id == fixtureId).Select(x => x.Id);
        var keys = await db.RoomPhotos.Where(x => rooms.Contains(x.RoomId)).Select(x => x.StorageKey).ToListAsync();
        keys.AddRange(await db.FixturePhotos.Where(x => fixtures.Contains(x.FixtureId)).Select(x => x.StorageKey).ToListAsync());
        if (propertyId is not null)
        {
            keys.AddRange(await db.PropertyPhotos.Where(x => x.PropertyId == propertyId).Select(x => x.StorageKey).ToListAsync());
            keys.AddRange(await db.Documents.Where(x => x.PropertyId == propertyId).Select(x => x.StorageKey).ToListAsync());
        }
        return keys;
    }
    // Removes stored files that no photo or document references any more (call after SaveChanges).
    static async Task DeleteUnusedFiles(InventoryDbContext db, FileStore store, IEnumerable<string> keys)
    {
        foreach (var key in keys.Where(FileStore.IsStoredKey).Distinct())
        {
            var used = await db.Documents.AnyAsync(x => x.StorageKey == key) || await db.PropertyPhotos.AnyAsync(x => x.StorageKey == key) || await db.RoomPhotos.AnyAsync(x => x.StorageKey == key)
                || await db.FixturePhotos.AnyAsync(x => x.StorageKey == key) || await db.AssetPhotos.AnyAsync(x => x.StorageKey == key);
            if (!used) store.Delete(key);
        }
    }
    static async Task<IResult?> ValidateDocument(DocumentInput input, InventoryDbContext db)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(input.Title)) errors["title"] = ["Title is required."];
        if (!Document.Kinds.Contains(input.Kind)) errors["kind"] = [$"Kind must be one of: {string.Join(", ", Document.Kinds)}."];
        if (string.IsNullOrWhiteSpace(input.StorageKey)) errors["storageKey"] = ["Upload a file first."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);
        if (!await db.Properties.AnyAsync(x => x.Id == input.PropertyId)) return Results.BadRequest("The selected property does not exist.");
        if (new[] { input.RoomId, input.FixtureId, input.AssetId, input.MaintenanceTaskId }.Count(x => x is not null) > 1) return Results.BadRequest("Attach a document to at most one room, fixture, asset or maintenance task.");
        if (input.RoomId is not null && !await db.Rooms.AnyAsync(x => x.Id == input.RoomId && x.PropertyId == input.PropertyId)) return Results.BadRequest("The room must be in the selected property.");
        if (input.FixtureId is not null && !await db.Fixtures.AnyAsync(x => x.Id == input.FixtureId && x.Room != null && x.Room.PropertyId == input.PropertyId)) return Results.BadRequest("The fixture must be in the selected property.");
        if (input.AssetId is not null && !await db.Assets.AnyAsync(x => x.Id == input.AssetId && x.PropertyId == input.PropertyId)) return Results.BadRequest("The asset must be in the selected property.");
        if (input.MaintenanceTaskId is not null && !await db.MaintenanceTasks.AnyAsync(x => x.Id == input.MaintenanceTaskId && x.PropertyId == input.PropertyId)) return Results.BadRequest("The maintenance task must be in the selected property.");
        return null;
    }
    static void ApplyDocument(DocumentInput x, Document d)
    {
        d.PropertyId = x.PropertyId;
        d.RoomId = x.RoomId;
        d.FixtureId = x.FixtureId;
        d.AssetId = x.AssetId;
        d.MaintenanceTaskId = x.MaintenanceTaskId;
        d.Title = x.Title.Trim();
        d.Kind = x.Kind;
        d.StorageKey = x.StorageKey.Trim();
        d.FileName = x.FileName?.Trim();
        d.ContentType = x.ContentType?.Trim();
        d.SizeBytes = x.SizeBytes;
        d.DocumentDate = x.DocumentDate;
        d.ExpiresOn = x.ExpiresOn;
        d.Tags = x.Tags?.Trim();
        d.Notes = x.Notes?.Trim();
    }
    static async Task<List<DocumentDto>> DocumentDtos(InventoryDbContext db, IQueryable<Document> documents)
    {
        var list = await documents.Include(x => x.Property).Include(x => x.Fixture).Include(x => x.Asset).Include(x => x.MaintenanceTask).ToListAsync();
        var roomPaths = list.Any(x => x.RoomId is not null) ? await RoomPaths(db) : [];
        string? AttachedTo(Document x) =>
            x.RoomId is Guid room ? roomPaths.GetValueOrDefault(room) :
            x.Fixture is not null ? $"Fixture: {x.Fixture.Name}" :
            x.Asset is not null ? $"Asset: {x.Asset.Name}" :
            x.MaintenanceTask is not null ? $"Maintenance: {x.MaintenanceTask.Title}" : null;
        return list.OrderByDescending(x => x.DocumentDate).ThenBy(x => x.Title)
            .Select(x => new DocumentDto(x.Id, x.PropertyId, x.RoomId, x.FixtureId, x.AssetId, x.MaintenanceTaskId, x.Title, x.Kind, x.StorageKey, x.FileName, x.ContentType, x.SizeBytes, x.DocumentDate, x.ExpiresOn, x.Tags, x.Notes, x.Property?.Name ?? "", AttachedTo(x)))
            .ToList();
    }
    // Adds a Moved event describing where the asset went, when its room or storage location actually changed.
    static async Task RecordMove(InventoryDbContext db, Asset asset, Guid? fromRoom, Guid? fromLocation)
    {
        if (fromRoom == asset.RoomId && fromLocation == asset.StorageLocationId) return;
        var rooms = await RoomPaths(db);
        var locations = (await LocationDtos(db, asset.PropertyId)).ToDictionary(x => x.Id, x => x.Path);
        string Label(Guid? room, Guid? location) => room is Guid r ? rooms.GetValueOrDefault(r) ?? "a room" : location is Guid l ? locations.GetValueOrDefault(l) ?? "storage" : "no location";
        db.AssetEvents.Add(new AssetEvent { AssetId = asset.Id, OccurredOn = Today(), Kind = "Moved", Description = $"{Label(fromRoom, fromLocation)} → {Label(asset.RoomId, asset.StorageLocationId)}" });
    }
    static async Task<Dictionary<Guid, string>> RoomPaths(InventoryDbContext db) =>
        await db.Rooms.Include(x => x.Floor).ThenInclude(x => x!.Property).ToDictionaryAsync(x => x.Id, x => x.Floor is null || x.Floor.Property is null ? x.Name : $"{x.Floor.Property.Name} · {x.Floor.Name} · {x.Name}");
    // Rooms using a paint: explicit library assignments plus painted surfaces whose colour code, or colour name and brand, match.
    static async Task<List<PaintUsageDto>> PaintUsage(InventoryDbContext db, Paint paint)
    {
        var roomPaths = await RoomPaths(db);
        var assigned = await db.RoomPaints.Where(x => x.PaintId == paint.Id).Select(x => new { x.RoomId, x.Surface }).ToListAsync();
        var code = Norm(paint.ColorCode);
        var surfaces = (await db.Surfaces.Where(x => x.SurfaceType != "flooring" && (x.ColorName != null || x.ColorCode != null)).ToListAsync())
            .Where(x => code != "" && Norm(x.ColorCode) == code || Norm(x.ColorName) == Norm(paint.ColorName) && (x.PaintBrand is null || Norm(x.PaintBrand) == Norm(paint.Brand)));
        return assigned.Select(x => new PaintUsageDto(x.RoomId, roomPaths.GetValueOrDefault(x.RoomId) ?? "", x.Surface, "Assigned"))
            .Concat(surfaces.Select(x => new PaintUsageDto(x.RoomId, roomPaths.GetValueOrDefault(x.RoomId) ?? "", x.Name, "Surface")))
            .OrderBy(x => x.RoomPath).ThenBy(x => x.Surface).ToList();
    }
    static async Task<List<FixtureDto>> FixtureDtos(InventoryDbContext db, IQueryable<Fixture>? fixtures = null)
    {
        var query = fixtures ?? db.Fixtures.AsQueryable();
        var rooms = await RoomPaths(db);
        var list = await query.OrderBy(x => x.Name).ToListAsync();
        return list.Select(x => ToDto(x, rooms.TryGetValue(x.RoomId, out var path) ? path : null)).ToList();
    }
    static Task<List<AssetDto>> AssetDtos(InventoryDbContext db, Guid? propertyId, bool archived) =>
        AssetDtos(db, db.Assets.Where(x => (propertyId == null || x.PropertyId == propertyId) && x.IsArchived == archived));
    static async Task<List<AssetDto>> AssetDtos(InventoryDbContext db, IQueryable<Asset> assets)
    {
        var list = await assets.ToListAsync();
        if (list.Count == 0) return [];
        var propertyIds = list.Select(x => x.PropertyId).Distinct().ToList();
        var locationPaths = (await db.StorageLocations.Where(x => propertyIds.Contains(x.PropertyId)).ToListAsync()) is var locations
            ? locations.ToDictionary(x => x.Id, x => Path(x, locations.ToDictionary(y => y.Id))) : [];
        var roomNames = await db.Rooms.Where(x => propertyIds.Contains(x.PropertyId)).ToDictionaryAsync(x => x.Id, x => x.Name);
        string? LocationPath(Asset x) =>
            x.StorageLocationId is Guid location ? locationPaths.GetValueOrDefault(location) :
            x.RoomId is Guid room ? roomNames.GetValueOrDefault(room) : null;
        return list.OrderBy(x => x.Name)
            .Select(x => new AssetDto(x.Id, x.PropertyId, x.RoomId, x.StorageLocationId, x.Name, x.Category, x.Description, x.Brand, x.Model, x.SerialNumber, x.PurchaseDate, x.PurchasePrice, x.CurrentValue, x.Condition, x.Notes, x.IsArchived, LocationPath(x)))
            .ToList();
    }
    static async Task<bool> IsDescendant(InventoryDbContext db, Guid candidateId, Guid ancestorId)
    {
        var parents = await db.StorageLocations.ToDictionaryAsync(x => x.Id, x => x.ParentId);
        var visited = new HashSet<Guid>();
        for (Guid? current = candidateId; current is not null && visited.Add(current.Value); current = parents.GetValueOrDefault(current.Value))
            if (current == ancestorId) return true;
        return false;
    }
    // "Parent → Child" display path; the visited set stops at a corrupt parent cycle instead of recursing forever.
    static string Path(StorageLocation item, IReadOnlyDictionary<Guid, StorageLocation> all)
    {
        var names = new List<string>();
        var visited = new HashSet<Guid>();
        for (var current = item; current is not null && visited.Add(current.Id); current = current.ParentId is Guid parent ? all.GetValueOrDefault(parent) : null)
            names.Add(current.Name);
        names.Reverse();
        return string.Join(" → ", names);
    }
    static async Task<string?> ValidateAsset(AssetInput input, InventoryDbContext db)
    {
        if (string.IsNullOrWhiteSpace(input.Name) || string.IsNullOrWhiteSpace(input.Category)) return "Asset name and category are required.";
        if (!await db.Properties.AnyAsync(x => x.Id == input.PropertyId)) return "The selected property does not exist.";
        if (input.RoomId is not null && input.StorageLocationId is not null) return "Choose either a room or a storage location, not both.";
        if (input.RoomId is not null && !await db.Rooms.AnyAsync(x => x.Id == input.RoomId && x.PropertyId == input.PropertyId)) return "Room must be in the selected property.";
        if (input.StorageLocationId is not null && !await db.StorageLocations.AnyAsync(x => x.Id == input.StorageLocationId && x.PropertyId == input.PropertyId)) return "Storage location must be in the selected property.";
        return null;
    }
    static Asset NewAsset(AssetInput input)
    {
        var asset = new Asset { Name = "", Category = "" };
        Apply(input, asset);
        return asset;
    }
    static void Apply(AssetInput x, Asset a)
    {
        a.PropertyId = x.PropertyId;
        a.RoomId = x.RoomId;
        a.StorageLocationId = x.StorageLocationId;
        a.Name = x.Name.Trim();
        a.Category = x.Category.Trim();
        a.Description = x.Description?.Trim();
        a.Brand = x.Brand?.Trim();
        a.Model = x.Model?.Trim();
        a.SerialNumber = x.SerialNumber?.Trim();
        a.PurchaseDate = x.PurchaseDate;
        a.PurchasePrice = x.PurchasePrice;
        a.CurrentValue = x.CurrentValue;
        a.Condition = x.Condition?.Trim();
        a.Notes = x.Notes?.Trim();
    }
    static async Task<InventoryExport> Export(InventoryDbContext db)
    {
        var props = await db.Properties.ToListAsync();
        var floors = await db.Floors.ToListAsync();
        var rooms = await db.Rooms.ToListAsync();
        var surfaces = await db.Surfaces.ToListAsync();
        var fixtures = await db.Fixtures.ToListAsync();
        var locs = await db.StorageLocations.ToListAsync();
        var assets = await db.Assets.Where(x => !x.IsArchived).ToListAsync();
        var propertyPhotos = await db.PropertyPhotos.ToListAsync();
        var assetPhotos = await db.AssetPhotos.Where(x => x.Asset != null && !x.Asset.IsArchived).ToListAsync();
        var paints = await db.Paints.ToListAsync();
        var roomPaints = await db.RoomPaints.ToListAsync();
        var roomPhotos = await db.RoomPhotos.ToListAsync();
        var fixturePhotos = await db.FixturePhotos.ToListAsync();
        var tasks = await db.MaintenanceTasks.ToListAsync();
        var records = await db.MaintenanceRecords.ToListAsync();
        var documents = await db.Documents.ToListAsync();
        var assetEvents = await db.AssetEvents.ToListAsync();
        // Records keep the backup ID they were imported with, so backup → restore → backup cycles produce the same IDs.
        static string Ext(IHasExternalId x) => x.ExternalId ?? x.Id.ToString();
        var propertyIds = props.ToDictionary(x => x.Id, Ext);
        var floorIds = floors.ToDictionary(x => x.Id, Ext);
        var roomIds = rooms.ToDictionary(x => x.Id, Ext);
        var locationIds = locs.ToDictionary(x => x.Id, Ext);
        var assetIds = assets.ToDictionary(x => x.Id, Ext);
        var fixtureIds = fixtures.ToDictionary(x => x.Id, Ext);
        var paintIds = paints.ToDictionary(x => x.Id, Ext);
        var taskIds = tasks.ToDictionary(x => x.Id, Ext);
        return new(1,
            props.Select(x => new ImportProperty(Ext(x), x.Name, x.Address, x.PurchaseDate, x.PurchasePrice, x.FloorArea, x.Notes, NormalizeCurrency(x.Currency) ?? "USD")).ToList(),
            floors.Select(x => new ImportFloor(Ext(x), propertyIds[x.PropertyId], x.Name, x.Notes)).ToList(),
            rooms.Select(x => new ImportRoom(Ext(x), x.FloorId is Guid floorId ? floorIds[floorId] : string.Empty, x.Name, x.Type, x.Area, x.Volume, x.CeilingHeight, x.Length, x.Width, x.Height, x.Flooring, x.WallFinish, x.CeilingFinish, x.PaintDetails, x.WindowsCount, x.DoorsCount, x.FixturesNotes, x.UtilitiesNotes, x.Notes)).ToList(),
            surfaces.Select(x => new ImportSurface(Ext(x), roomIds[x.RoomId], x.Name, x.SurfaceType, x.PaintBrand, x.ColorName, x.ColorCode, x.Finish, x.Coats, x.PaintedDate, x.Painter, x.QuantityPurchased, x.Manufacturer, x.ProductName, x.Material, x.Supplier, x.Warranty, x.Invoice, x.InstallationDate, x.Notes, x.SortOrder)).ToList(),
            locs.Select(x => new ImportStorageLocation(Ext(x), propertyIds[x.PropertyId], x.ParentId is Guid parentId ? locationIds[parentId] : null, x.Name, x.Type)).ToList(),
            assets.Select(x => new ImportAsset(Ext(x), propertyIds[x.PropertyId], x.RoomId is Guid roomId ? roomIds[roomId] : null, x.StorageLocationId is Guid locationId ? locationIds[locationId] : null, x.Name, x.Category, x.Description, x.Brand, x.Model, x.SerialNumber, x.PurchaseDate, x.PurchasePrice, x.CurrentValue, x.Condition, x.Notes)).ToList(),
            propertyPhotos.Select(x => new ImportPropertyPhoto(Ext(x), propertyIds[x.PropertyId], x.StorageKey, x.Caption, x.SortOrder)).ToList(),
            fixtures.Select(x => new ImportFixture(Ext(x), roomIds[x.RoomId], x.Name, x.Type, x.Manufacturer, x.Model, x.SerialNumber, x.PurchaseDate, x.PurchasePrice, x.CurrentValue, x.Warranty, x.ManualUrl, x.InstallerName, x.InstallationDate, x.MaintenanceSchedule, x.LastMaintenanceDate, x.Condition, x.Notes, x.Category, x.Provider, x.AccountNumber)).ToList(),
            assetPhotos.Select(x => new ImportAssetPhoto(Ext(x), assetIds[x.AssetId], x.StorageKey, x.Caption, x.SortOrder)).ToList(),
            paints.Select(x => new ImportPaint(Ext(x), x.Brand, x.ColorName, x.ColorCode, x.Finish, x.Notes)).ToList(),
            roomPaints.Select(x => new ImportRoomPaint($"{roomIds[x.RoomId]}:{paintIds[x.PaintId]}", roomIds[x.RoomId], paintIds[x.PaintId], x.SortOrder, x.Surface)).ToList(),
            roomPhotos.Select(x => new ImportRoomPhoto(Ext(x), roomIds[x.RoomId], x.StorageKey, x.Caption, x.SortOrder)).ToList(),
            fixturePhotos.Select(x => new ImportFixturePhoto(Ext(x), fixtureIds[x.FixtureId], x.StorageKey, x.Caption, x.SortOrder)).ToList(),
            tasks.Select(x => new ImportMaintenanceTask(Ext(x), propertyIds[x.PropertyId], x.FixtureId is Guid fixtureId ? fixtureIds[fixtureId] : null, x.Title, x.IntervalValue, x.IntervalUnit, x.DueOn, x.LastCompletedOn, x.Supplier, x.EstimatedCost, x.Notes)).ToList(),
            records.Select(x => new ImportMaintenanceRecord(Ext(x), taskIds[x.TaskId], x.CompletedOn, x.Cost, x.Supplier, x.Notes)).ToList(),
            // Documents attached to an archived asset (not exported) fall back to property level in the backup.
            documents.Select(x => new ImportDocument(Ext(x), propertyIds[x.PropertyId], x.RoomId is Guid r ? roomIds[r] : null, x.FixtureId is Guid f ? fixtureIds[f] : null, x.AssetId is Guid a && assetIds.TryGetValue(a, out var assetExt) ? assetExt : null, x.MaintenanceTaskId is Guid t ? taskIds[t] : null, x.Title, x.Kind, x.StorageKey, x.FileName, x.ContentType, x.SizeBytes, x.DocumentDate, x.ExpiresOn, x.Tags, x.Notes)).ToList(),
            // History of archived assets stays out, like the assets themselves.
            assetEvents.Where(x => assetIds.ContainsKey(x.AssetId)).Select(x => new ImportAssetEvent(Ext(x), assetIds[x.AssetId], x.OccurredOn, x.Kind, x.Description, x.Cost)).ToList());
    }
    // Maps every existing row of one type by its backup ID and its database ID, so imports can recognise records they already hold.
    static async Task<Dictionary<string, Guid>> ExistingIds<T>(IQueryable<T> rows) where T : class, IHasExternalId
    {
        var map = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in await rows.Select(x => new { x.Id, x.ExternalId }).ToListAsync())
        {
            map.TryAdd(row.Id.ToString(), row.Id);
            if (row.ExternalId is not null) map.TryAdd(row.ExternalId, row.Id);
        }
        return map;
    }
    static async Task<ImportPreviewDto> Preview(InventoryExport i, InventoryDbContext db)
    {
        var errors = new List<string>();
        if (i.SchemaVersion != 1) errors.Add("Only schemaVersion 1 is supported.");
        var fixtures = i.Fixtures ?? [];
        var assetPhotos = i.AssetPhotos ?? [];
        var paints = i.Paints ?? [];
        var roomPaints = i.RoomPaints ?? [];
        var roomPhotos = i.RoomPhotos ?? [];
        var fixturePhotos = i.FixturePhotos ?? [];
        var tasks = i.MaintenanceTasks ?? [];
        var records = i.MaintenanceRecords ?? [];
        var documents = i.Documents ?? [];
        var assetEvents = i.AssetEvents ?? [];
        var ids = i.Properties.Select(x => x.ExternalId).Concat(i.Floors.Select(x => x.ExternalId)).Concat(i.Rooms.Select(x => x.ExternalId)).Concat(i.Surfaces.Select(x => x.ExternalId))
            .Concat(i.StorageLocations.Select(x => x.ExternalId)).Concat(i.Assets.Select(x => x.ExternalId)).Concat(i.PropertyPhotos.Select(x => x.ExternalId)).Concat(fixtures.Select(x => x.ExternalId))
            .Concat(assetPhotos.Select(x => x.ExternalId)).Concat(paints.Select(x => x.ExternalId)).Concat(roomPaints.Select(x => x.ExternalId)).Concat(roomPhotos.Select(x => x.ExternalId))
            .Concat(fixturePhotos.Select(x => x.ExternalId)).Concat(tasks.Select(x => x.ExternalId)).Concat(records.Select(x => x.ExternalId)).Concat(documents.Select(x => x.ExternalId)).Concat(assetEvents.Select(x => x.ExternalId)).ToList();
        if (ids.Any(string.IsNullOrWhiteSpace) || ids.Count != ids.Distinct().Count()) errors.Add("Every record needs a unique externalId.");
        var propIds = i.Properties.Select(x => x.ExternalId).ToHashSet();
        var floorIds = i.Floors.Select(x => x.ExternalId).ToHashSet();
        var roomIds = i.Rooms.Select(x => x.ExternalId).ToHashSet();
        var locIds = i.StorageLocations.Select(x => x.ExternalId).ToHashSet();
        var assetIds = i.Assets.Select(x => x.ExternalId).ToHashSet();
        var fixtureIds = fixtures.Select(x => x.ExternalId).ToHashSet();
        var paintIds = paints.Select(x => x.ExternalId).ToHashSet();
        if (i.Properties.Any(x => string.IsNullOrWhiteSpace(x.Name)) || i.Assets.Any(x => string.IsNullOrWhiteSpace(x.Name) || string.IsNullOrWhiteSpace(x.Category))) errors.Add("Properties and assets require names; assets also require categories.");
        if (i.Properties.Any(x => x.Currency is not null && NormalizeCurrency(x.Currency) is null)) errors.Add("Property currency must be a three-letter ISO code.");
        if (i.PropertyPhotos.Any(x => string.IsNullOrWhiteSpace(x.StorageKey) || string.IsNullOrWhiteSpace(x.PropertyExternalId))) errors.Add("Property photos require a storage key and a referenced property.");
        if (fixtures.Any(x => string.IsNullOrWhiteSpace(x.RoomExternalId) || string.IsNullOrWhiteSpace(x.Name) || string.IsNullOrWhiteSpace(x.Type))) errors.Add("Fixtures require a room reference, name, and type.");
        if (assetPhotos.Any(x => string.IsNullOrWhiteSpace(x.AssetExternalId))) errors.Add("Asset photos require a referenced asset.");
        if (paints.Any(x => string.IsNullOrWhiteSpace(x.Brand) || string.IsNullOrWhiteSpace(x.ColorName))) errors.Add("Paints require a brand and colour name.");
        if (roomPhotos.Any(x => string.IsNullOrWhiteSpace(x.StorageKey)) || fixturePhotos.Any(x => string.IsNullOrWhiteSpace(x.StorageKey))) errors.Add("Room and fixture photos require a storage key.");
        if (roomPaints.GroupBy(x => (x.RoomExternalId, x.PaintExternalId)).Any(x => x.Count() > 1)) errors.Add("A paint can only be assigned to a room once.");
        var taskIds = tasks.Select(x => x.ExternalId).ToHashSet();
        if (tasks.Any(x => string.IsNullOrWhiteSpace(x.Title) || (x.IntervalValue is not null || x.IntervalUnit is not null) && (x.IntervalValue is not > 0 || !IntervalUnits.Contains(x.IntervalUnit?.ToLowerInvariant()))))
            errors.Add("Maintenance tasks require a title, and a repeat interval must be a positive number of days, months or years.");
        var fixtureRooms = fixtures.GroupBy(x => x.ExternalId).ToDictionary(x => x.Key, x => x.First().RoomExternalId);
        var roomFloors = i.Rooms.GroupBy(x => x.ExternalId).ToDictionary(x => x.Key, x => x.First().FloorExternalId);
        var floorProperties = i.Floors.GroupBy(x => x.ExternalId).ToDictionary(x => x.Key, x => x.First().PropertyExternalId);
        if (tasks.Any(x => !propIds.Contains(x.PropertyExternalId) || x.FixtureExternalId is not null && !fixtureIds.Contains(x.FixtureExternalId)) || records.Any(x => !taskIds.Contains(x.TaskExternalId)))
            errors.Add("Maintenance tasks must reference an imported property (and fixture, if any), and service records an imported task.");
        else if (tasks.Any(x => x.FixtureExternalId is not null && fixtureRooms.TryGetValue(x.FixtureExternalId, out var room) && roomFloors.TryGetValue(room, out var floor) && floorProperties.TryGetValue(floor, out var property) && property != x.PropertyExternalId))
            errors.Add("A maintenance task's fixture must be in the task's property.");
        if (documents.Any(x => string.IsNullOrWhiteSpace(x.Title) || string.IsNullOrWhiteSpace(x.StorageKey) || !Document.Kinds.Contains(x.Kind)))
            errors.Add($"Documents require a title, a storage key and a kind ({string.Join(", ", Document.Kinds)}).");
        if (documents.Any(x => !propIds.Contains(x.PropertyExternalId) || x.RoomExternalId is not null && !roomIds.Contains(x.RoomExternalId) || x.FixtureExternalId is not null && !fixtureIds.Contains(x.FixtureExternalId)
            || x.AssetExternalId is not null && !assetIds.Contains(x.AssetExternalId) || x.MaintenanceTaskExternalId is not null && !taskIds.Contains(x.MaintenanceTaskExternalId)))
            errors.Add("Documents must reference an imported property and, if attached, an imported room, fixture, asset or maintenance task.");
        if (documents.Any(x => new[] { x.RoomExternalId, x.FixtureExternalId, x.AssetExternalId, x.MaintenanceTaskExternalId }.Count(y => y is not null) > 1))
            errors.Add("A document can be attached to at most one room, fixture, asset or maintenance task.");
        if (assetEvents.Any(x => !assetIds.Contains(x.AssetExternalId) || !AssetEvent.AutomaticKinds.Contains(x.Kind) && !AssetEvent.ManualKinds.Contains(x.Kind)))
            errors.Add("Asset events must reference an imported asset and use a known kind.");
        if (i.Floors.Any(x => !propIds.Contains(x.PropertyExternalId)) || i.Rooms.Any(x => !floorIds.Contains(x.FloorExternalId)) || i.StorageLocations.Any(x => !propIds.Contains(x.PropertyExternalId))
            || i.Assets.Any(x => !propIds.Contains(x.PropertyExternalId)) || i.PropertyPhotos.Any(x => !propIds.Contains(x.PropertyExternalId)) || i.Surfaces.Any(x => !roomIds.Contains(x.RoomExternalId))
            || fixtures.Any(x => !roomIds.Contains(x.RoomExternalId)) || assetPhotos.Any(x => !assetIds.Contains(x.AssetExternalId)) || roomPhotos.Any(x => !roomIds.Contains(x.RoomExternalId))
            || fixturePhotos.Any(x => !fixtureIds.Contains(x.FixtureExternalId)) || roomPaints.Any(x => !roomIds.Contains(x.RoomExternalId) || !paintIds.Contains(x.PaintExternalId)))
            errors.Add("Every floor, room, surface, location, asset, fixture, photo, and room paint must reference an imported parent record.");
        if (i.Assets.Any(x => x.RoomExternalId is not null && !roomIds.Contains(x.RoomExternalId) || x.StorageLocationExternalId is not null && !locIds.Contains(x.StorageLocationExternalId))) errors.Add("Assets reference an unknown room or storage location.");
        var parents = i.StorageLocations.GroupBy(x => x.ExternalId).ToDictionary(x => x.Key, x => x.First().ParentExternalId);
        if (parents.Values.Any(x => x is not null && !parents.ContainsKey(x))) errors.Add("Storage locations reference a parent location that isn't in the file.");
        else if (parents.Keys.Any(start => { var seen = new HashSet<string>(); for (var current = start; current is not null; current = parents[current]) if (!seen.Add(current)) return true; return false; })) errors.Add("Storage locations form a parent cycle.");
        // Records whose backup ID already exists are reused on import; duplicate assets are also reported individually.
        var existingAssets = await ExistingIds(db.Assets);
        var duplicates = i.Assets.Select(x => x.ExternalId).Where(existingAssets.ContainsKey).ToList();
        var existingCount = duplicates.Count
            + await CountExisting(db.Properties, i.Properties.Select(x => x.ExternalId)) + await CountExisting(db.Floors, i.Floors.Select(x => x.ExternalId))
            + await CountExisting(db.Rooms, i.Rooms.Select(x => x.ExternalId)) + await CountExisting(db.Surfaces, i.Surfaces.Select(x => x.ExternalId))
            + await CountExisting(db.StorageLocations, i.StorageLocations.Select(x => x.ExternalId)) + await CountExisting(db.Fixtures, fixtures.Select(x => x.ExternalId))
            + await CountExisting(db.Paints, paints.Select(x => x.ExternalId)) + await CountExisting(db.PropertyPhotos, i.PropertyPhotos.Select(x => x.ExternalId))
            + await CountExisting(db.AssetPhotos, assetPhotos.Select(x => x.ExternalId)) + await CountExisting(db.RoomPhotos, roomPhotos.Select(x => x.ExternalId))
            + await CountExisting(db.FixturePhotos, fixturePhotos.Select(x => x.ExternalId))
            + await CountExisting(db.MaintenanceTasks, tasks.Select(x => x.ExternalId)) + await CountExisting(db.MaintenanceRecords, records.Select(x => x.ExternalId))
            + await CountExisting(db.Documents, documents.Select(x => x.ExternalId)) + await CountExisting(db.AssetEvents, assetEvents.Select(x => x.ExternalId));
        var photoCount = i.PropertyPhotos.Count + assetPhotos.Count + roomPhotos.Count + fixturePhotos.Count;
        return new(errors.Count == 0, errors, i.Properties.Count, i.Floors.Count, i.Rooms.Count, i.Surfaces.Count, i.StorageLocations.Count, i.Assets.Count, duplicates, fixtures.Count, paints.Count, photoCount, existingCount, tasks.Count, documents.Count);
    }
    static async Task<int> CountExisting<T>(IQueryable<T> rows, IEnumerable<string> externalIds) where T : class, IHasExternalId
    {
        var existing = await ExistingIds(rows);
        return externalIds.Count(existing.ContainsKey);
    }
    static string? NormalizeCurrency(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "USD";
        var normalized = value.Trim().ToUpperInvariant();
        return normalized.Length == 3 && normalized.All(char.IsLetter) ? normalized : null;
    }
    static string Norm(string? value)=>(value??"").Trim().ToLowerInvariant();
}
