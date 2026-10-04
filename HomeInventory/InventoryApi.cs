using System.Text.Json;
using HomeInventory.Client;
using Microsoft.EntityFrameworkCore;

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
        api.MapDelete("/properties/{id:guid}", async (Guid id, InventoryDbContext db) =>
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
            db.Properties.Remove(entity);
            await db.SaveChangesAsync();
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
        api.MapDelete("/floors/{id:guid}", async (Guid id, InventoryDbContext db) =>
        {
            var entity = await db.Floors.FindAsync(id);
            if (entity is null) return Results.NotFound();
            var assetCount = await db.Assets.CountAsync(x => x.Room != null && x.Room.FloorId == id);
            if (assetCount > 0) return Results.BadRequest($"This floor still has {assetCount} asset(s) in its rooms. Move them before deleting the floor.");
            db.Floors.Remove(entity);
            await db.SaveChangesAsync();
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
            entity.FloorId = input.FloorId;
            entity.PropertyId = (await db.Floors.FindAsync(input.FloorId))!.PropertyId;
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
        api.MapDelete("/rooms/{id:guid}", async (Guid id, InventoryDbContext db) =>
        {
            var entity = await db.Rooms.FindAsync(id);
            if (entity is null) return Results.NotFound();
            var assetCount = await db.Assets.CountAsync(x => x.RoomId == id);
            if (assetCount > 0) return Results.BadRequest($"This room still holds {assetCount} asset(s). Move them before deleting the room.");
            db.Rooms.Remove(entity);
            await db.SaveChangesAsync();
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
                Notes = input.Notes?.Trim()
            };
            db.Fixtures.Add(entity);
            await db.SaveChangesAsync();
            return Results.Created($"/api/fixtures/{entity.Id}", (await FixtureDtos(db, db.Fixtures.Where(x => x.Id == entity.Id))).Single());
        });
        api.MapGet("/fixtures", async (Guid? roomId, Guid? propertyId, InventoryDbContext db) =>
        {
            var fixtures = db.Fixtures.AsQueryable();
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
            if (!await db.Rooms.AnyAsync(x => x.Id == input.RoomId)) return Results.BadRequest("The selected room does not exist.");
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
            await db.SaveChangesAsync();
            return Results.Ok((await FixtureDtos(db, db.Fixtures.Where(x => x.Id == entity.Id))).Single());
        });
        api.MapDelete("/fixtures/{id:guid}", async (Guid id, InventoryDbContext db) =>
        {
            var entity = await db.Fixtures.FindAsync(id);
            if (entity is null) return Results.NotFound();
            db.Fixtures.Remove(entity);
            await db.SaveChangesAsync();
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
        api.MapDelete("/fixtures/{id:guid}/photos/{photoId:guid}", async (Guid id, Guid photoId, InventoryDbContext db) =>
        {
            var entity = await db.FixturePhotos.SingleOrDefaultAsync(x => x.Id == photoId && x.FixtureId == id);
            if (entity is null) return Results.NotFound();
            db.FixturePhotos.Remove(entity);
            await db.SaveChangesAsync();
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
        api.MapPut("/properties/{id:guid}/photos/{photoId:guid}", async (Guid id, Guid photoId, PhotoMetadataInput input, InventoryDbContext db) =>
        {
            var entity = await db.PropertyPhotos.SingleOrDefaultAsync(x => x.Id == photoId && x.PropertyId == id); if (entity is null) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(input.StorageKey)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["storageKey"] = ["Storage key is required."] });
            entity.StorageKey = input.StorageKey.Trim(); entity.Caption = input.Caption?.Trim(); entity.SortOrder = input.SortOrder; await db.SaveChangesAsync(); return Results.Ok(ToDto(entity));
        });
        api.MapDelete("/properties/{id:guid}/photos/{photoId:guid}", async (Guid id, Guid photoId, InventoryDbContext db) =>
        {
            var entity = await db.PropertyPhotos.SingleOrDefaultAsync(x => x.Id == photoId && x.PropertyId == id); if (entity is null) return Results.NotFound();
            db.PropertyPhotos.Remove(entity); await db.SaveChangesAsync(); return Results.NoContent();
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
        api.MapPut("/rooms/{id:guid}/photos/{photoId:guid}", async (Guid id, Guid photoId, PhotoMetadataInput input, InventoryDbContext db) =>
        {
            var entity = await db.RoomPhotos.SingleOrDefaultAsync(x => x.Id == photoId && x.RoomId == id); if (entity is null) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(input.StorageKey)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["storageKey"] = ["Storage key is required."] });
            entity.StorageKey = input.StorageKey.Trim(); entity.Caption = input.Caption?.Trim(); entity.SortOrder = input.SortOrder; await db.SaveChangesAsync(); return Results.Ok(ToDto(entity));
        });
        api.MapDelete("/rooms/{id:guid}/photos/{photoId:guid}", async (Guid id, Guid photoId, InventoryDbContext db) =>
        {
            var entity = await db.RoomPhotos.SingleOrDefaultAsync(x => x.Id == photoId && x.RoomId == id); if (entity is null) return Results.NotFound();
            db.RoomPhotos.Remove(entity); await db.SaveChangesAsync(); return Results.NoContent();
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
            await db.SaveChangesAsync();
            return Results.Created($"/api/assets/{entity.Id}", (await AssetDtos(db, input.PropertyId, false)).Single(x => x.Id == entity.Id));
        });
        api.MapPut("/assets/{id:guid}", async (Guid id, AssetInput input, InventoryDbContext db) =>
        {
            var entity = await db.Assets.FindAsync(id);
            if (entity is null) return Results.NotFound();
            var error = await ValidateAsset(input, db);
            if (error is not null) return Results.BadRequest(error);
            Apply(input, entity);
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
            entity.RoomId = input.RoomId;
            entity.StorageLocationId = input.StorageLocationId;
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
        api.MapPost("/assets/{id:guid}/archive", async (Guid id, InventoryDbContext db) =>
        {
            var entity = await db.Assets.FindAsync(id);
            if (entity is null) return Results.NotFound();
            entity.IsArchived = true;
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
        api.MapPost("/assets/{id:guid}/unarchive", async (Guid id, InventoryDbContext db) =>
        {
            var entity = await db.Assets.FindAsync(id);
            if (entity is null) return Results.NotFound();
            entity.IsArchived = false;
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
        api.MapDelete("/assets/{id:guid}/photos/{photoId:guid}", async (Guid id, Guid photoId, InventoryDbContext db) =>
        {
            var entity = await db.AssetPhotos.SingleOrDefaultAsync(x => x.Id == photoId && x.AssetId == id);
            if (entity is null) return Results.NotFound();
            db.AssetPhotos.Remove(entity);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        api.MapGet("/dashboard", async (InventoryDbContext db) =>
        {
            // TODO: Include due maintenance, expiring warranties, missing receipts, recent purchases, and room completion metrics.
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
            return new DashboardDto(assets.Count, fixtures.Count, totals);
        });
        api.MapGet("/search", async (string? q, InventoryDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(q)) return Results.Ok(Array.Empty<SearchResultDto>());
            var term = q.Trim().ToLower();
            var assets = await AssetDtos(db, null, false);
            var fixtures = await FixtureDtos(db, db.Fixtures.AsQueryable());
            var locations = await LocationDtos(db, null);
            var roomPaths = await RoomPaths(db);
            var paints = await db.Paints.ToListAsync();
            var surfaces = await db.Surfaces.ToListAsync();
            var paintResults = new List<SearchResultDto>();
            foreach (var paint in paints.Where(x => $"{x.Brand} {x.ColorName} {x.ColorCode} {x.Finish} {x.Notes}".ToLower().Contains(term)))
            {
                var usedIn = (await PaintUsage(db, paint)).Select(x => x.RoomPath).Distinct().ToList();
                paintResults.Add(new SearchResultDto("Paint", paint.Id, $"{paint.Brand} {paint.ColorName}", string.Join(" ", new[] { paint.ColorCode, paint.Finish }.Where(x => !string.IsNullOrWhiteSpace(x))), usedIn.Count == 0 ? null : string.Join(", ", usedIn)));
            }
            var surfaceResults = surfaces.Where(x => $"{x.Name} {x.SurfaceType} {x.PaintBrand} {x.ColorName} {x.ColorCode} {x.Finish} {x.Material} {x.Manufacturer} {x.ProductName} {x.Notes}".ToLower().Contains(term))
                .Select(x => new SearchResultDto("Surface", x.Id, x.Name, string.Join(" · ", new[] { x.SurfaceType, x.SurfaceType == "flooring" ? x.Material : x.PaintBrand, x.ColorName }.Where(y => !string.IsNullOrWhiteSpace(y))), roomPaths.GetValueOrDefault(x.RoomId)));
            var results = assets.Where(x => $"{x.Name} {x.Category} {x.Brand} {x.Model} {x.SerialNumber} {x.Notes}".ToLower().Contains(term)).Select(x => new SearchResultDto("Asset", x.Id, x.Name, x.Category, x.LocationPath))
                .Concat(fixtures.Where(x => $"{x.Name} {x.Type} {x.Manufacturer} {x.Model} {x.SerialNumber} {x.Notes}".ToLower().Contains(term)).Select(x => new SearchResultDto("Fixture", x.Id, x.Name, x.Type, x.LocationPath)))
                .Concat(locations.Where(x => $"{x.Name} {x.Type} {x.Path}".ToLower().Contains(term)).Select(x => new SearchResultDto("Storage", x.Id, x.Name, x.Type ?? "Storage location", x.Path)))
                .Concat(paintResults).Concat(surfaceResults).Take(50);
            return Results.Ok(results);
        });

        api.MapGet("/export", async (InventoryDbContext db) => Results.Ok(await Export(db)));
        api.MapPost("/import/preview", async (InventoryExport import, InventoryDbContext db) => Results.Ok(await Preview(import, db)));
        api.MapPost("/import/confirm", async (ImportConfirmation confirmation, InventoryDbContext db) =>
        {
            var preview = await Preview(confirmation.Inventory, db);
            if (!preview.IsValid) return Results.BadRequest(preview);
            await using var transaction = await db.Database.BeginTransactionAsync();
            var properties = new Dictionary<string, Guid>();
            var floors = new Dictionary<string, Guid>();
            var rooms = new Dictionary<string, Guid>();
            var locations = new Dictionary<string, Guid>();
            var assets = new Dictionary<string, Guid>();
            var fixtures = new Dictionary<string, Guid>();
            var paints = new Dictionary<string, Guid>();

            foreach (var property in confirmation.Inventory.Properties)
            {
                var currency = NormalizeCurrency(property.Currency) ?? "USD";
                var entity = new Property { Name = property.Name, Address = property.Address, PurchaseDate = property.PurchaseDate, PurchasePrice = property.PurchasePrice, FloorArea = property.FloorArea, Currency = currency, Notes = property.Notes };
                db.Properties.Add(entity);
                properties[property.ExternalId] = entity.Id;
            }
            foreach (var photo in confirmation.Inventory.PropertyPhotos) db.PropertyPhotos.Add(new PropertyPhoto { PropertyId = properties[photo.PropertyExternalId], StorageKey = photo.StorageKey?.Trim() ?? string.Empty, Caption = photo.Caption?.Trim(), SortOrder = photo.SortOrder });
            foreach (var floor in confirmation.Inventory.Floors)
            {
                var entity = new Floor { PropertyId = properties[floor.PropertyExternalId], Name = floor.Name, Notes = floor.Notes };
                db.Floors.Add(entity);
                floors[floor.ExternalId] = entity.Id;
            }
            foreach (var room in confirmation.Inventory.Rooms)
            {
                var entity = new Room
                {
                    PropertyId = properties[confirmation.Inventory.Floors.Single(x => x.ExternalId == room.FloorExternalId).PropertyExternalId],
                    FloorId = floors[room.FloorExternalId],
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
            foreach (var surface in confirmation.Inventory.Surfaces) db.Surfaces.Add(new Surface { RoomId = rooms[surface.RoomExternalId], Name = surface.Name, SurfaceType = surface.SurfaceType, PaintBrand = surface.PaintBrand, ColorName = surface.ColorName, ColorCode = surface.ColorCode, Finish = surface.Finish, Coats = surface.Coats, PaintedDate = surface.PaintedDate, Painter = surface.Painter, QuantityPurchased = surface.QuantityPurchased, Manufacturer = surface.Manufacturer, ProductName = surface.ProductName, Material = surface.Material, Supplier = surface.Supplier, Warranty = surface.Warranty, Invoice = surface.Invoice, InstallationDate = surface.InstallationDate, Notes = surface.Notes, SortOrder = surface.SortOrder });
            foreach (var location in confirmation.Inventory.StorageLocations)
            {
                var entity = new StorageLocation { PropertyId = properties[location.PropertyExternalId], ParentId = location.ParentExternalId is null ? null : locations[location.ParentExternalId], Name = location.Name, Type = location.Type };
                db.StorageLocations.Add(entity);
                locations[location.ExternalId] = entity.Id;
            }
            foreach (var fixture in confirmation.Inventory.Fixtures ?? [])
            {
                var entity = new Fixture { RoomId = rooms[fixture.RoomExternalId], Name = fixture.Name, Type = fixture.Type, Manufacturer = fixture.Manufacturer, Model = fixture.Model, SerialNumber = fixture.SerialNumber, PurchaseDate = fixture.PurchaseDate, PurchasePrice = fixture.PurchasePrice, CurrentValue = fixture.CurrentValue, Warranty = fixture.Warranty, ManualUrl = fixture.ManualUrl, InstallerName = fixture.InstallerName, InstallationDate = fixture.InstallationDate, MaintenanceSchedule = fixture.MaintenanceSchedule, LastMaintenanceDate = fixture.LastMaintenanceDate, Condition = fixture.Condition, Notes = fixture.Notes };
                db.Fixtures.Add(entity);
                fixtures[fixture.ExternalId] = entity.Id;
            }
            foreach (var photo in confirmation.Inventory.FixturePhotos ?? []) db.FixturePhotos.Add(new FixturePhoto { FixtureId = fixtures[photo.FixtureExternalId], StorageKey = photo.StorageKey?.Trim() ?? string.Empty, Caption = photo.Caption?.Trim(), SortOrder = photo.SortOrder });
            foreach (var photo in confirmation.Inventory.RoomPhotos ?? []) db.RoomPhotos.Add(new RoomPhoto { RoomId = rooms[photo.RoomExternalId], StorageKey = photo.StorageKey?.Trim() ?? string.Empty, Caption = photo.Caption?.Trim(), SortOrder = photo.SortOrder });
            // The paint library is global, so reuse an existing paint with the same brand, colour name and code instead of duplicating it.
            var existingPaints = await db.Paints.ToListAsync();
            foreach (var paint in confirmation.Inventory.Paints ?? [])
            {
                var match = existingPaints.FirstOrDefault(x => Norm(x.Brand) == Norm(paint.Brand) && Norm(x.ColorName) == Norm(paint.ColorName) && Norm(x.ColorCode) == Norm(paint.ColorCode));
                if (match is null)
                {
                    match = new Paint { Brand = paint.Brand.Trim(), ColorName = paint.ColorName.Trim(), ColorCode = paint.ColorCode?.Trim(), Finish = paint.Finish?.Trim(), Notes = paint.Notes?.Trim() };
                    db.Paints.Add(match);
                    existingPaints.Add(match);
                }
                paints[paint.ExternalId] = match.Id;
            }
            foreach (var roomPaint in (confirmation.Inventory.RoomPaints ?? []).DistinctBy(x => (rooms[x.RoomExternalId], paints[x.PaintExternalId])))
                db.RoomPaints.Add(new RoomPaint { RoomId = rooms[roomPaint.RoomExternalId], PaintId = paints[roomPaint.PaintExternalId], SortOrder = roomPaint.SortOrder, Surface = roomPaint.Surface?.Trim() });
            foreach (var asset in confirmation.Inventory.Assets.Where(x => !confirmation.SkipExternalIds.Contains(x.ExternalId)))
            {
                var entity = new Asset { PropertyId = properties[asset.PropertyExternalId], RoomId = asset.RoomExternalId is null ? null : rooms[asset.RoomExternalId], StorageLocationId = asset.StorageLocationExternalId is null ? null : locations[asset.StorageLocationExternalId], Name = asset.Name, Category = asset.Category, Description = asset.Description, Brand = asset.Brand, Model = asset.Model, SerialNumber = asset.SerialNumber, PurchaseDate = asset.PurchaseDate, PurchasePrice = asset.PurchasePrice, CurrentValue = asset.CurrentValue, Condition = asset.Condition, Notes = asset.Notes, ExternalId = asset.ExternalId };
                db.Assets.Add(entity);
                assets[asset.ExternalId] = entity.Id;
            }
            foreach (var photo in (confirmation.Inventory.AssetPhotos ?? []).Where(x => assets.ContainsKey(x.AssetExternalId))) db.AssetPhotos.Add(new AssetPhoto { AssetId = assets[photo.AssetExternalId], StorageKey = photo.StorageKey?.Trim() ?? string.Empty, Caption = photo.Caption?.Trim(), SortOrder = photo.SortOrder });
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return Results.NoContent();
        });
        return api;
    }

    public sealed record ImportConfirmation(InventoryExport Inventory, List<string> SkipExternalIds);
    static PropertyDto ToDto(Property x) => new(x.Id, x.Name, x.Address, x.PurchaseDate, x.PurchasePrice, x.FloorArea, x.Notes, x.Currency);
    static FixtureDto ToDto(Fixture x, string? locationPath) => new(x.Id, x.RoomId, x.Name, x.Type, x.Manufacturer, x.Model, x.SerialNumber, x.PurchaseDate, x.PurchasePrice, x.CurrentValue, x.Warranty, x.ManualUrl, x.InstallerName, x.InstallationDate, x.MaintenanceSchedule, x.LastMaintenanceDate, x.Condition, x.Notes, locationPath);
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
    static async Task<List<StorageLocationDto>> LocationDtos(InventoryDbContext db, Guid? propertyId) { var list = await db.StorageLocations.Where(x => propertyId == null || x.PropertyId == propertyId).ToListAsync(); return list.Select(x => new StorageLocationDto(x.Id, x.PropertyId, x.ParentId, x.Name, x.Type, Path(x, list))).OrderBy(x => x.Path).ToList(); }
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
    static async Task<List<AssetDto>> AssetDtos(InventoryDbContext db, Guid? propertyId, bool archived) { var locations = await LocationDtos(db, propertyId); var roomNames = await db.Rooms.Where(x => propertyId == null || x.Floor != null && x.Floor.PropertyId == propertyId).ToDictionaryAsync(x => x.Id, x => x.Name); var list = await db.Assets.Where(x => (propertyId == null || x.PropertyId == propertyId) && x.IsArchived == archived).ToListAsync(); return list.OrderBy(x => x.Name).Select(x => new AssetDto(x.Id,x.PropertyId,x.RoomId,x.StorageLocationId,x.Name,x.Category,x.Description,x.Brand,x.Model,x.SerialNumber,x.PurchaseDate,x.PurchasePrice,x.CurrentValue,x.Condition,x.Notes,x.IsArchived,x.StorageLocationId is not null ? locations.SingleOrDefault(l => l.Id == x.StorageLocationId)?.Path : x.RoomId is not null && roomNames.TryGetValue(x.RoomId.Value, out var n) ? n : null)).ToList(); }
    static async Task<bool> IsDescendant(InventoryDbContext db, Guid candidateId, Guid ancestorId)
    {
        var parents = await db.StorageLocations.ToDictionaryAsync(x => x.Id, x => x.ParentId);
        var visited = new HashSet<Guid>();
        for (Guid? current = candidateId; current is not null && visited.Add(current.Value); current = parents.GetValueOrDefault(current.Value))
            if (current == ancestorId) return true;
        return false;
    }
    static string Path(StorageLocation item, List<StorageLocation> all) => item.ParentId is null ? item.Name : $"{Path(all.Single(x => x.Id == item.ParentId), all)} → {item.Name}";
    static async Task<string?> ValidateAsset(AssetInput input, InventoryDbContext db) { if (string.IsNullOrWhiteSpace(input.Name) || string.IsNullOrWhiteSpace(input.Category)) return "Asset name and category are required."; if (!await db.Properties.AnyAsync(x => x.Id == input.PropertyId)) return "The selected property does not exist."; if (input.RoomId is not null && input.StorageLocationId is not null) return "Choose either a room or a storage location, not both."; if (input.RoomId is not null && !await db.Rooms.AnyAsync(x => x.Id == input.RoomId && x.Floor != null && x.Floor.PropertyId == input.PropertyId)) return "Room must be in the selected property."; if (input.StorageLocationId is not null && !await db.StorageLocations.AnyAsync(x => x.Id == input.StorageLocationId && x.PropertyId == input.PropertyId)) return "Storage location must be in the selected property."; return null; }
    static Asset NewAsset(AssetInput x) { var a = new Asset { Name = "", Category = "" }; Apply(x, a); return a; }
    static void Apply(AssetInput x, Asset a) { a.PropertyId=x.PropertyId;a.RoomId=x.RoomId;a.StorageLocationId=x.StorageLocationId;a.Name=x.Name.Trim();a.Category=x.Category.Trim();a.Description=x.Description?.Trim();a.Brand=x.Brand?.Trim();a.Model=x.Model?.Trim();a.SerialNumber=x.SerialNumber?.Trim();a.PurchaseDate=x.PurchaseDate;a.PurchasePrice=x.PurchasePrice;a.CurrentValue=x.CurrentValue;a.Condition=x.Condition?.Trim();a.Notes=x.Notes?.Trim(); }
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
        var assetExternalIds = assets.ToDictionary(x => x.Id, x => x.ExternalId ?? x.Id.ToString());
        return new(1,
            props.Select(x => new ImportProperty(x.Id.ToString(), x.Name, x.Address, x.PurchaseDate, x.PurchasePrice, x.FloorArea, x.Notes, NormalizeCurrency(x.Currency) ?? "USD")).ToList(),
            floors.Select(x => new ImportFloor(x.Id.ToString(), x.PropertyId.ToString(), x.Name, x.Notes)).ToList(),
            rooms.Select(x => new ImportRoom(x.Id.ToString(), x.FloorId?.ToString() ?? string.Empty, x.Name, x.Type, x.Area, x.Volume, x.CeilingHeight, x.Length, x.Width, x.Height, x.Flooring, x.WallFinish, x.CeilingFinish, x.PaintDetails, x.WindowsCount, x.DoorsCount, x.FixturesNotes, x.UtilitiesNotes, x.Notes)).ToList(),
            surfaces.Select(x => new ImportSurface(x.Id.ToString(), x.RoomId.ToString(), x.Name, x.SurfaceType, x.PaintBrand, x.ColorName, x.ColorCode, x.Finish, x.Coats, x.PaintedDate, x.Painter, x.QuantityPurchased, x.Manufacturer, x.ProductName, x.Material, x.Supplier, x.Warranty, x.Invoice, x.InstallationDate, x.Notes, x.SortOrder)).ToList(),
            locs.Select(x => new ImportStorageLocation(x.Id.ToString(), x.PropertyId.ToString(), x.ParentId?.ToString(), x.Name, x.Type)).ToList(),
            assets.Select(x => new ImportAsset(x.ExternalId ?? x.Id.ToString(), x.PropertyId.ToString(), x.RoomId?.ToString(), x.StorageLocationId?.ToString(), x.Name, x.Category, x.Description, x.Brand, x.Model, x.SerialNumber, x.PurchaseDate, x.PurchasePrice, x.CurrentValue, x.Condition, x.Notes)).ToList(),
            propertyPhotos.Select(x => new ImportPropertyPhoto(x.Id.ToString(), x.PropertyId.ToString(), x.StorageKey, x.Caption, x.SortOrder)).ToList(),
            fixtures.Select(x => new ImportFixture(x.Id.ToString(), x.RoomId.ToString(), x.Name, x.Type, x.Manufacturer, x.Model, x.SerialNumber, x.PurchaseDate, x.PurchasePrice, x.CurrentValue, x.Warranty, x.ManualUrl, x.InstallerName, x.InstallationDate, x.MaintenanceSchedule, x.LastMaintenanceDate, x.Condition, x.Notes)).ToList(),
            assetPhotos.Select(x => new ImportAssetPhoto(x.Id.ToString(), assetExternalIds[x.AssetId], x.StorageKey, x.Caption, x.SortOrder)).ToList(),
            paints.Select(x => new ImportPaint(x.Id.ToString(), x.Brand, x.ColorName, x.ColorCode, x.Finish, x.Notes)).ToList(),
            roomPaints.Select(x => new ImportRoomPaint($"{x.RoomId}:{x.PaintId}", x.RoomId.ToString(), x.PaintId.ToString(), x.SortOrder, x.Surface)).ToList(),
            roomPhotos.Select(x => new ImportRoomPhoto(x.Id.ToString(), x.RoomId.ToString(), x.StorageKey, x.Caption, x.SortOrder)).ToList(),
            fixturePhotos.Select(x => new ImportFixturePhoto(x.Id.ToString(), x.FixtureId.ToString(), x.StorageKey, x.Caption, x.SortOrder)).ToList());
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
        var ids = i.Properties.Select(x => x.ExternalId).Concat(i.Floors.Select(x => x.ExternalId)).Concat(i.Rooms.Select(x => x.ExternalId)).Concat(i.Surfaces.Select(x => x.ExternalId))
            .Concat(i.StorageLocations.Select(x => x.ExternalId)).Concat(i.Assets.Select(x => x.ExternalId)).Concat(i.PropertyPhotos.Select(x => x.ExternalId)).Concat(fixtures.Select(x => x.ExternalId))
            .Concat(assetPhotos.Select(x => x.ExternalId)).Concat(paints.Select(x => x.ExternalId)).Concat(roomPaints.Select(x => x.ExternalId)).Concat(roomPhotos.Select(x => x.ExternalId))
            .Concat(fixturePhotos.Select(x => x.ExternalId)).ToList();
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
        if (i.Floors.Any(x => !propIds.Contains(x.PropertyExternalId)) || i.Rooms.Any(x => !floorIds.Contains(x.FloorExternalId)) || i.StorageLocations.Any(x => !propIds.Contains(x.PropertyExternalId))
            || i.Assets.Any(x => !propIds.Contains(x.PropertyExternalId)) || i.PropertyPhotos.Any(x => !propIds.Contains(x.PropertyExternalId)) || i.Surfaces.Any(x => !roomIds.Contains(x.RoomExternalId))
            || fixtures.Any(x => !roomIds.Contains(x.RoomExternalId)) || assetPhotos.Any(x => !assetIds.Contains(x.AssetExternalId)) || roomPhotos.Any(x => !roomIds.Contains(x.RoomExternalId))
            || fixturePhotos.Any(x => !fixtureIds.Contains(x.FixtureExternalId)) || roomPaints.Any(x => !roomIds.Contains(x.RoomExternalId) || !paintIds.Contains(x.PaintExternalId)))
            errors.Add("Every floor, room, surface, location, asset, fixture, photo, and room paint must reference an imported parent record.");
        if (i.Assets.Any(x => x.RoomExternalId is not null && !roomIds.Contains(x.RoomExternalId) || x.StorageLocationExternalId is not null && !locIds.Contains(x.StorageLocationExternalId))) errors.Add("Assets reference an unknown room or storage location.");
        // An asset is a duplicate when an active asset was imported with, or exported as, the same external ID.
        var importIds = i.Assets.Select(x => x.ExternalId).ToList();
        var existing = await db.Assets.Where(x => !x.IsArchived).Select(x => new { x.Id, x.ExternalId }).ToListAsync();
        var existingIds = existing.Select(x => x.ExternalId ?? x.Id.ToString()).Concat(existing.Select(x => x.Id.ToString())).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var duplicates = importIds.Where(existingIds.Contains).ToList();
        var photoCount = i.PropertyPhotos.Count + assetPhotos.Count + roomPhotos.Count + fixturePhotos.Count;
        return new(errors.Count == 0, errors, i.Properties.Count, i.Floors.Count, i.Rooms.Count, i.Surfaces.Count, i.StorageLocations.Count, i.Assets.Count, duplicates, fixtures.Count, paints.Count, photoCount);
    }
    static string? NormalizeCurrency(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "USD";
        var normalized = value.Trim().ToUpperInvariant();
        return normalized.Length == 3 && normalized.All(char.IsLetter) ? normalized : null;
    }
    static string Norm(string? value)=>(value??"").Trim().ToLowerInvariant();
}
