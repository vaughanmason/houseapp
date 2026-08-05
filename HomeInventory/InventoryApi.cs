using System.Text.Json;
using HomeInventory.Client;
using Microsoft.EntityFrameworkCore;

namespace HomeInventory;

public static class InventoryApi
{
    public static RouteGroupBuilder MapInventoryApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");
        // TODO: Add endpoint groups for floors, room surfaces/fixtures, maintenance, utilities, paint, documents, and photos.
        api.MapGet("/properties", async (InventoryDbContext db) => await db.Properties.OrderBy(x => x.Name).Select(x => ToDto(x)).ToListAsync());
        api.MapPost("/properties", async (PropertyInput input, InventoryDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(input.Name)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Name is required."] });
            var entity = new Property { Name = input.Name.Trim(), Address = input.Address?.Trim(), PurchaseDate = input.PurchaseDate, PurchasePrice = input.PurchasePrice, FloorArea = input.FloorArea, Notes = input.Notes?.Trim() };
            db.Properties.Add(entity); await db.SaveChangesAsync(); return Results.Created($"/api/properties/{entity.Id}", ToDto(entity));
        });
        api.MapPut("/properties/{id:guid}", async (Guid id, PropertyInput input, InventoryDbContext db) =>
        {
            var entity = await db.Properties.FindAsync(id); if (entity is null) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(input.Name)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Name is required."] });
            entity.Name = input.Name.Trim(); entity.Address = input.Address?.Trim(); entity.PurchaseDate = input.PurchaseDate; entity.PurchasePrice = input.PurchasePrice; entity.FloorArea = input.FloorArea; entity.Notes = input.Notes?.Trim(); await db.SaveChangesAsync(); return Results.Ok(ToDto(entity));
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
                PropertyId = floor?.PropertyId,
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
            entity.PropertyId = (await db.Floors.FindAsync(input.FloorId))?.PropertyId;
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
            entity.PropertyId=input.PropertyId;entity.ParentId=input.ParentId;entity.Name=input.Name.Trim();entity.Type=input.Type?.Trim();await db.SaveChangesAsync();return Results.Ok((await LocationDtos(db,input.PropertyId)).Single(x=>x.Id==id));
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

        api.MapGet("/dashboard", async (InventoryDbContext db) =>
        {
            // TODO: Include due maintenance, expiring warranties, missing receipts, recent purchases, and room completion metrics.
            var assets = db.Assets.Where(x => !x.IsArchived);
            var categories = (await assets.GroupBy(x => x.Category).Select(x => new { Category = x.Key, Total = x.Sum(a => a.CurrentValue ?? 0) }).OrderByDescending(x => x.Total).ToListAsync())
                .Select(x => new CategoryTotalDto(x.Category, x.Total)).ToList();
            return new DashboardDto(await assets.CountAsync(), categories.Sum(x => x.Total), categories);
        });
        api.MapGet("/search", async (string? q, InventoryDbContext db) =>
        {
            // TODO: Search planned documents, warranties, paint, fixtures, utilities, and OCR text in addition to assets and storage.
            if (string.IsNullOrWhiteSpace(q)) return Results.Ok(Array.Empty<SearchResultDto>());
            var term = q.Trim().ToLower();
            var assets = await AssetDtos(db, null, false);
            var locations = await LocationDtos(db, null);
            var results = assets.Where(x => $"{x.Name} {x.Category} {x.Brand} {x.Model} {x.SerialNumber} {x.Notes}".ToLower().Contains(term)).Select(x => new SearchResultDto("Asset", x.Id, x.Name, x.Category, x.LocationPath))
                .Concat(locations.Where(x => $"{x.Name} {x.Type} {x.Path}".ToLower().Contains(term)).Select(x => new SearchResultDto("Storage", x.Id, x.Name, x.Type ?? "Storage location", x.Path))).Take(50);
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

            foreach (var property in confirmation.Inventory.Properties)
            {
                var entity = new Property { Name = property.Name, Address = property.Address, PurchaseDate = property.PurchaseDate, PurchasePrice = property.PurchasePrice, FloorArea = property.FloorArea, Notes = property.Notes };
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
                    PropertyId = floors.TryGetValue(room.FloorExternalId, out var floorId) ? (await db.Floors.FindAsync(floorId))?.PropertyId : null,
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
            foreach (var asset in confirmation.Inventory.Assets.Where(x => !confirmation.SkipExternalIds.Contains(x.ExternalId))) db.Assets.Add(new Asset { PropertyId = properties[asset.PropertyExternalId], RoomId = asset.RoomExternalId is null ? null : rooms[asset.RoomExternalId], StorageLocationId = asset.StorageLocationExternalId is null ? null : locations[asset.StorageLocationExternalId], Name = asset.Name, Category = asset.Category, Description = asset.Description, Brand = asset.Brand, Model = asset.Model, SerialNumber = asset.SerialNumber, PurchaseDate = asset.PurchaseDate, PurchasePrice = asset.PurchasePrice, CurrentValue = asset.CurrentValue, Condition = asset.Condition, Notes = asset.Notes });
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return Results.NoContent();
        });
        return api;
    }

    public sealed record ImportConfirmation(InventoryExport Inventory, List<string> SkipExternalIds);
    static PropertyDto ToDto(Property x) => new(x.Id, x.Name, x.Address, x.PurchaseDate, x.PurchasePrice, x.FloorArea, x.Notes);
    static FloorDto ToDto(Floor x) => new(x.Id, x.PropertyId, x.Name, x.Notes);
    static RoomDto ToDto(Room x) => new(x.Id, x.FloorId ?? Guid.Empty, x.Name, x.Type, x.Area, x.Volume, x.CeilingHeight, x.Length, x.Width, x.Height, x.Flooring, x.WallFinish, x.CeilingFinish, x.PaintDetails, x.WindowsCount, x.DoorsCount, x.FixturesNotes, x.UtilitiesNotes, x.Notes);
    static SurfaceDto ToDto(Surface x) => new(x.Id, x.RoomId, x.Name, x.SurfaceType, x.PaintBrand, x.ColorName, x.ColorCode, x.Finish, x.Coats, x.PaintedDate, x.Painter, x.QuantityPurchased, x.Manufacturer, x.ProductName, x.Material, x.Supplier, x.Warranty, x.Invoice, x.InstallationDate, x.Notes, x.SortOrder);
    static PaintDto ToDto(Paint x) => new(x.Id, x.Brand, x.ColorName, x.ColorCode, x.Finish, x.Notes);
    static PhotoMetadataDto ToDto(PropertyPhoto x) => new(x.Id, x.StorageKey, x.Caption, x.SortOrder);
    static PhotoMetadataDto ToDto(RoomPhoto x) => new(x.Id, x.StorageKey, x.Caption, x.SortOrder);
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
    static async Task<List<AssetDto>> AssetDtos(InventoryDbContext db, Guid? propertyId, bool archived) { var locations = await LocationDtos(db, propertyId); var roomNames = await db.Rooms.Where(x => propertyId == null || x.Floor != null && x.Floor.PropertyId == propertyId).ToDictionaryAsync(x => x.Id, x => x.Name); var list = await db.Assets.Where(x => (propertyId == null || x.PropertyId == propertyId) && x.IsArchived == archived).ToListAsync(); return list.OrderBy(x => x.Name).Select(x => new AssetDto(x.Id,x.PropertyId,x.RoomId,x.StorageLocationId,x.Name,x.Category,x.Description,x.Brand,x.Model,x.SerialNumber,x.PurchaseDate,x.PurchasePrice,x.CurrentValue,x.Condition,x.Notes,x.IsArchived,x.StorageLocationId is not null ? locations.SingleOrDefault(l => l.Id == x.StorageLocationId)?.Path : x.RoomId is not null && roomNames.TryGetValue(x.RoomId.Value, out var n) ? n : null)).ToList(); }
    static string Path(StorageLocation item, List<StorageLocation> all) => item.ParentId is null ? item.Name : $"{Path(all.Single(x => x.Id == item.ParentId), all)} → {item.Name}";
    static async Task<string?> ValidateAsset(AssetInput input, InventoryDbContext db) { if (string.IsNullOrWhiteSpace(input.Name) || string.IsNullOrWhiteSpace(input.Category)) return "Asset name and category are required."; if (!await db.Properties.AnyAsync(x => x.Id == input.PropertyId)) return "The selected property does not exist."; if (input.RoomId is not null && !await db.Rooms.AnyAsync(x => x.Id == input.RoomId && x.Floor != null && x.Floor.PropertyId == input.PropertyId)) return "Room must be in the selected property."; if (input.StorageLocationId is not null && !await db.StorageLocations.AnyAsync(x => x.Id == input.StorageLocationId && x.PropertyId == input.PropertyId)) return "Storage location must be in the selected property."; return null; }
    static Asset NewAsset(AssetInput x) { var a = new Asset { Name = "", Category = "" }; Apply(x, a); return a; }
    static void Apply(AssetInput x, Asset a) { a.PropertyId=x.PropertyId;a.RoomId=x.RoomId;a.StorageLocationId=x.StorageLocationId;a.Name=x.Name.Trim();a.Category=x.Category.Trim();a.Description=x.Description?.Trim();a.Brand=x.Brand?.Trim();a.Model=x.Model?.Trim();a.SerialNumber=x.SerialNumber?.Trim();a.PurchaseDate=x.PurchaseDate;a.PurchasePrice=x.PurchasePrice;a.CurrentValue=x.CurrentValue;a.Condition=x.Condition?.Trim();a.Notes=x.Notes?.Trim(); }
    static async Task<InventoryExport> Export(InventoryDbContext db) { var props=await db.Properties.ToListAsync();var floors=await db.Floors.ToListAsync();var rooms=await db.Rooms.ToListAsync();var surfaces=await db.Surfaces.ToListAsync();var locs=await db.StorageLocations.ToListAsync();var assets=await db.Assets.ToListAsync();var propertyPhotos=await db.PropertyPhotos.ToListAsync();return new(1,props.Select(x=>new ImportProperty(x.Id.ToString(),x.Name,x.Address,x.PurchaseDate,x.PurchasePrice,x.FloorArea,x.Notes)).ToList(),floors.Select(x=>new ImportFloor(x.Id.ToString(),x.PropertyId.ToString(),x.Name,x.Notes)).ToList(),rooms.Select(x=>new ImportRoom(x.Id.ToString(),x.FloorId?.ToString() ?? string.Empty,x.Name,x.Type,x.Area,x.Volume,x.CeilingHeight,x.Length,x.Width,x.Height,x.Flooring,x.WallFinish,x.CeilingFinish,x.PaintDetails,x.WindowsCount,x.DoorsCount,x.FixturesNotes,x.UtilitiesNotes,x.Notes)).ToList(),surfaces.Select(x=>new ImportSurface(x.Id.ToString(),x.RoomId.ToString(),x.Name,x.SurfaceType,x.PaintBrand,x.ColorName,x.ColorCode,x.Finish,x.Coats,x.PaintedDate,x.Painter,x.QuantityPurchased,x.Manufacturer,x.ProductName,x.Material,x.Supplier,x.Warranty,x.Invoice,x.InstallationDate,x.Notes,x.SortOrder)).ToList(),locs.Select(x=>new ImportStorageLocation(x.Id.ToString(),x.PropertyId.ToString(),x.ParentId?.ToString(),x.Name,x.Type)).ToList(),assets.Where(x=>!x.IsArchived).Select(x=>new ImportAsset(x.Id.ToString(),x.PropertyId.ToString(),x.RoomId?.ToString(),x.StorageLocationId?.ToString(),x.Name,x.Category,x.Description,x.Brand,x.Model,x.SerialNumber,x.PurchaseDate,x.PurchasePrice,x.CurrentValue,x.Condition,x.Notes)).ToList(),propertyPhotos.Select(x=>new ImportPropertyPhoto(x.Id.ToString(),x.PropertyId.ToString(),x.StorageKey,x.Caption,x.SortOrder)).ToList()); }
    static async Task<ImportPreviewDto> Preview(InventoryExport i, InventoryDbContext db) { var errors=new List<string>(); if(i.SchemaVersion!=1) errors.Add("Only schemaVersion 1 is supported."); var ids=i.Properties.Select(x=>x.ExternalId).Concat(i.Floors.Select(x=>x.ExternalId)).Concat(i.Rooms.Select(x=>x.ExternalId)).Concat(i.Surfaces.Select(x=>x.ExternalId)).Concat(i.StorageLocations.Select(x=>x.ExternalId)).Concat(i.Assets.Select(x=>x.ExternalId)).Concat(i.PropertyPhotos.Select(x=>x.ExternalId)).ToList();if(ids.Any(string.IsNullOrWhiteSpace)||ids.Count!=ids.Distinct().Count()) errors.Add("Every record needs a unique externalId.");var propIds=i.Properties.Select(x=>x.ExternalId).ToHashSet();var floorIds=i.Floors.Select(x=>x.ExternalId).ToHashSet();var roomIds=i.Rooms.Select(x=>x.ExternalId).ToHashSet();var surfaceIds=i.Surfaces.Select(x=>x.ExternalId).ToHashSet();var locIds=i.StorageLocations.Select(x=>x.ExternalId).ToHashSet();if(i.Properties.Any(x=>string.IsNullOrWhiteSpace(x.Name))||i.Assets.Any(x=>string.IsNullOrWhiteSpace(x.Name)||string.IsNullOrWhiteSpace(x.Category))) errors.Add("Properties and assets require names; assets also require categories.");if(i.PropertyPhotos.Any(x=>string.IsNullOrWhiteSpace(x.StorageKey)||string.IsNullOrWhiteSpace(x.PropertyExternalId))) errors.Add("Property photos require a storage key and a referenced property.");if(i.Floors.Any(x=>!propIds.Contains(x.PropertyExternalId))||i.Rooms.Any(x=>!floorIds.Contains(x.FloorExternalId))||i.StorageLocations.Any(x=>!propIds.Contains(x.PropertyExternalId))||i.Assets.Any(x=>!propIds.Contains(x.PropertyExternalId))||i.PropertyPhotos.Any(x=>!propIds.Contains(x.PropertyExternalId))||i.Surfaces.Any(x=>!roomIds.Contains(x.RoomExternalId))) errors.Add("Every floor, room, surface, location, asset, and property photo must reference an imported parent record.");if(i.Assets.Any(x=>x.RoomExternalId is not null&&!roomIds.Contains(x.RoomExternalId)||x.StorageLocationExternalId is not null&&!locIds.Contains(x.StorageLocationExternalId))) errors.Add("Assets reference an unknown room or storage location.");var existing=await AssetDtos(db,null,false);var duplicates=i.Assets.Where(a=>existing.Any(e=>Norm(e.Name)==Norm(a.Name)&&Norm(e.LocationPath)==Norm(LocationImportPath(a,i)))).Select(x=>x.ExternalId).ToList();return new(errors.Count==0,errors,i.Properties.Count,i.Floors.Count,i.Rooms.Count,i.Surfaces.Count,i.StorageLocations.Count,i.Assets.Count,duplicates); }
    static string? LocationImportPath(ImportAsset a, InventoryExport i)
    {
        if (a.StorageLocationExternalId is null) return a.RoomExternalId is null ? null : i.Rooms.SingleOrDefault(x => x.ExternalId == a.RoomExternalId)?.Name;
        var locations = i.StorageLocations.ToDictionary(x => x.ExternalId);
        string Build(string id) { var location = locations[id]; return location.ParentExternalId is null ? location.Name : $"{Build(location.ParentExternalId)} → {location.Name}"; }
        return locations.ContainsKey(a.StorageLocationExternalId) ? Build(a.StorageLocationExternalId) : null;
    }
    static string Norm(string? value)=>(value??"").Trim().ToLowerInvariant();
}
