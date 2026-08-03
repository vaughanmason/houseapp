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
            var entity = new Property { Name = input.Name.Trim(), Address = input.Address?.Trim(), FloorArea = input.FloorArea, Notes = input.Notes?.Trim() };
            db.Properties.Add(entity); await db.SaveChangesAsync(); return Results.Created($"/api/properties/{entity.Id}", ToDto(entity));
        });
        api.MapPut("/properties/{id:guid}", async (Guid id, PropertyInput input, InventoryDbContext db) =>
        {
            var entity = await db.Properties.FindAsync(id); if (entity is null) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(input.Name)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Name is required."] });
            entity.Name = input.Name.Trim(); entity.Address = input.Address?.Trim(); entity.FloorArea = input.FloorArea; entity.Notes = input.Notes?.Trim(); await db.SaveChangesAsync(); return Results.Ok(ToDto(entity));
        });

        api.MapGet("/rooms", async (Guid? propertyId, InventoryDbContext db) => await db.Rooms.Where(x => propertyId == null || x.PropertyId == propertyId).OrderBy(x => x.Name).Select(x => ToDto(x)).ToListAsync());
        api.MapPost("/rooms", async (RoomInput input, InventoryDbContext db) =>
        {
            if (!await db.Properties.AnyAsync(x => x.Id == input.PropertyId)) return Results.BadRequest("The selected property does not exist.");
            if (string.IsNullOrWhiteSpace(input.Name)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Name is required."] });
            var entity = new Room { PropertyId = input.PropertyId, Name = input.Name.Trim(), Type = input.Type?.Trim(), Length = input.Length, Width = input.Width, Height = input.Height, Notes = input.Notes?.Trim() };
            db.Rooms.Add(entity); await db.SaveChangesAsync(); return Results.Created($"/api/rooms/{entity.Id}", ToDto(entity));
        });
        api.MapPut("/rooms/{id:guid}", async (Guid id, RoomInput input, InventoryDbContext db) =>
        {
            var entity = await db.Rooms.FindAsync(id); if (entity is null) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(input.Name) || !await db.Properties.AnyAsync(x => x.Id == input.PropertyId)) return Results.BadRequest("A valid property and room name are required.");
            entity.PropertyId=input.PropertyId; entity.Name=input.Name.Trim(); entity.Type=input.Type?.Trim(); entity.Length=input.Length; entity.Width=input.Width; entity.Height=input.Height; entity.Notes=input.Notes?.Trim(); await db.SaveChangesAsync(); return Results.Ok(ToDto(entity));
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
            var error = await ValidateAsset(input, db); if (error is not null) return Results.BadRequest(error);
            var entity = NewAsset(input); db.Assets.Add(entity); await db.SaveChangesAsync(); return Results.Created($"/api/assets/{entity.Id}", (await AssetDtos(db, input.PropertyId, false)).Single(x => x.Id == entity.Id));
        });
        api.MapPut("/assets/{id:guid}", async (Guid id, AssetInput input, InventoryDbContext db) =>
        {
            var entity = await db.Assets.FindAsync(id); if (entity is null) return Results.NotFound();
            var error = await ValidateAsset(input, db); if (error is not null) return Results.BadRequest(error);
            Apply(input, entity); await db.SaveChangesAsync(); return Results.Ok((await AssetDtos(db, input.PropertyId, entity.IsArchived)).Single(x => x.Id == id));
        });
        api.MapPost("/assets/{id:guid}/move", async (Guid id, AssetMoveInput input, InventoryDbContext db) =>
        {
            var entity = await db.Assets.FindAsync(id); if (entity is null) return Results.NotFound();
            if (input.RoomId is not null && !await db.Rooms.AnyAsync(x => x.Id == input.RoomId && x.PropertyId == entity.PropertyId)) return Results.BadRequest("Room must be in the asset property.");
            if (input.StorageLocationId is not null && !await db.StorageLocations.AnyAsync(x => x.Id == input.StorageLocationId && x.PropertyId == entity.PropertyId)) return Results.BadRequest("Storage location must be in the asset property.");
            entity.RoomId = input.RoomId; entity.StorageLocationId = input.StorageLocationId; await db.SaveChangesAsync(); return Results.NoContent();
        });
        api.MapPost("/assets/{id:guid}/archive", async (Guid id, InventoryDbContext db) => { var entity = await db.Assets.FindAsync(id); if (entity is null) return Results.NotFound(); entity.IsArchived = true; await db.SaveChangesAsync(); return Results.NoContent(); });

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
        // TODO: Add CSV and HomeBox-compatible import/export while preserving the versioned JSON backup contract.
        api.MapPost("/import/preview", async (InventoryExport import, InventoryDbContext db) => Results.Ok(await Preview(import, db)));
        api.MapPost("/import/confirm", async (ImportConfirmation confirmation, InventoryDbContext db) =>
        {
            var preview = await Preview(confirmation.Inventory, db); if (!preview.IsValid) return Results.BadRequest(preview);
            await using var transaction = await db.Database.BeginTransactionAsync();
            var properties = new Dictionary<string, Guid>(); var rooms = new Dictionary<string, Guid>(); var locations = new Dictionary<string, Guid>();
            foreach (var p in confirmation.Inventory.Properties) { var entity = new Property { Name = p.Name, Address = p.Address, FloorArea = p.FloorArea, Notes = p.Notes }; db.Properties.Add(entity); properties[p.ExternalId] = entity.Id; }
            foreach (var r in confirmation.Inventory.Rooms) { var entity = new Room { PropertyId = properties[r.PropertyExternalId], Name = r.Name, Type = r.Type, Length = r.Length, Width = r.Width, Height = r.Height, Notes = r.Notes }; db.Rooms.Add(entity); rooms[r.ExternalId] = entity.Id; }
            foreach (var l in confirmation.Inventory.StorageLocations) { var entity = new StorageLocation { PropertyId = properties[l.PropertyExternalId], ParentId = l.ParentExternalId is null ? null : locations[l.ParentExternalId], Name = l.Name, Type = l.Type }; db.StorageLocations.Add(entity); locations[l.ExternalId] = entity.Id; }
            foreach (var a in confirmation.Inventory.Assets.Where(x => !confirmation.SkipExternalIds.Contains(x.ExternalId))) db.Assets.Add(new Asset { PropertyId = properties[a.PropertyExternalId], RoomId = a.RoomExternalId is null ? null : rooms[a.RoomExternalId], StorageLocationId = a.StorageLocationExternalId is null ? null : locations[a.StorageLocationExternalId], Name = a.Name, Category = a.Category, Description = a.Description, Brand = a.Brand, Model = a.Model, SerialNumber = a.SerialNumber, PurchaseDate = a.PurchaseDate, PurchasePrice = a.PurchasePrice, CurrentValue = a.CurrentValue, Condition = a.Condition, Notes = a.Notes });
            await db.SaveChangesAsync(); await transaction.CommitAsync(); return Results.NoContent();
        });
        return api;
    }

    public sealed record ImportConfirmation(InventoryExport Inventory, List<string> SkipExternalIds);
    static PropertyDto ToDto(Property x) => new(x.Id, x.Name, x.Address, x.FloorArea, x.Notes);
    static RoomDto ToDto(Room x) => new(x.Id, x.PropertyId, x.Name, x.Type, x.Length, x.Width, x.Height, x.Notes);
    static async Task<List<StorageLocationDto>> LocationDtos(InventoryDbContext db, Guid? propertyId) { var list = await db.StorageLocations.Where(x => propertyId == null || x.PropertyId == propertyId).ToListAsync(); return list.Select(x => new StorageLocationDto(x.Id, x.PropertyId, x.ParentId, x.Name, x.Type, Path(x, list))).OrderBy(x => x.Path).ToList(); }
    static async Task<List<AssetDto>> AssetDtos(InventoryDbContext db, Guid? propertyId, bool archived) { var locations = await LocationDtos(db, propertyId); var roomNames = await db.Rooms.Where(x => propertyId == null || x.PropertyId == propertyId).ToDictionaryAsync(x => x.Id, x => x.Name); var list = await db.Assets.Where(x => (propertyId == null || x.PropertyId == propertyId) && x.IsArchived == archived).ToListAsync(); return list.OrderBy(x => x.Name).Select(x => new AssetDto(x.Id,x.PropertyId,x.RoomId,x.StorageLocationId,x.Name,x.Category,x.Description,x.Brand,x.Model,x.SerialNumber,x.PurchaseDate,x.PurchasePrice,x.CurrentValue,x.Condition,x.Notes,x.IsArchived,x.StorageLocationId is not null ? locations.SingleOrDefault(l => l.Id == x.StorageLocationId)?.Path : x.RoomId is not null && roomNames.TryGetValue(x.RoomId.Value, out var n) ? n : null)).ToList(); }
    static string Path(StorageLocation item, List<StorageLocation> all) => item.ParentId is null ? item.Name : $"{Path(all.Single(x => x.Id == item.ParentId), all)} → {item.Name}";
    static async Task<string?> ValidateAsset(AssetInput input, InventoryDbContext db) { if (string.IsNullOrWhiteSpace(input.Name) || string.IsNullOrWhiteSpace(input.Category)) return "Asset name and category are required."; if (!await db.Properties.AnyAsync(x => x.Id == input.PropertyId)) return "The selected property does not exist."; if (input.RoomId is not null && !await db.Rooms.AnyAsync(x => x.Id == input.RoomId && x.PropertyId == input.PropertyId)) return "Room must be in the selected property."; if (input.StorageLocationId is not null && !await db.StorageLocations.AnyAsync(x => x.Id == input.StorageLocationId && x.PropertyId == input.PropertyId)) return "Storage location must be in the selected property."; return null; }
    static Asset NewAsset(AssetInput x) { var a = new Asset { Name = "", Category = "" }; Apply(x, a); return a; }
    static void Apply(AssetInput x, Asset a) { a.PropertyId=x.PropertyId;a.RoomId=x.RoomId;a.StorageLocationId=x.StorageLocationId;a.Name=x.Name.Trim();a.Category=x.Category.Trim();a.Description=x.Description?.Trim();a.Brand=x.Brand?.Trim();a.Model=x.Model?.Trim();a.SerialNumber=x.SerialNumber?.Trim();a.PurchaseDate=x.PurchaseDate;a.PurchasePrice=x.PurchasePrice;a.CurrentValue=x.CurrentValue;a.Condition=x.Condition?.Trim();a.Notes=x.Notes?.Trim(); }
    static async Task<InventoryExport> Export(InventoryDbContext db) { var props=await db.Properties.ToListAsync();var rooms=await db.Rooms.ToListAsync();var locs=await db.StorageLocations.ToListAsync();var assets=await db.Assets.ToListAsync();return new(1,props.Select(x=>new ImportProperty(x.Id.ToString(),x.Name,x.Address,x.FloorArea,x.Notes)).ToList(),rooms.Select(x=>new ImportRoom(x.Id.ToString(),x.PropertyId.ToString(),x.Name,x.Type,x.Length,x.Width,x.Height,x.Notes)).ToList(),locs.Select(x=>new ImportStorageLocation(x.Id.ToString(),x.PropertyId.ToString(),x.ParentId?.ToString(),x.Name,x.Type)).ToList(),assets.Where(x=>!x.IsArchived).Select(x=>new ImportAsset(x.Id.ToString(),x.PropertyId.ToString(),x.RoomId?.ToString(),x.StorageLocationId?.ToString(),x.Name,x.Category,x.Description,x.Brand,x.Model,x.SerialNumber,x.PurchaseDate,x.PurchasePrice,x.CurrentValue,x.Condition,x.Notes)).ToList()); }
    static async Task<ImportPreviewDto> Preview(InventoryExport i, InventoryDbContext db) { var errors=new List<string>(); if(i.SchemaVersion!=1) errors.Add("Only schemaVersion 1 is supported."); var ids=i.Properties.Select(x=>x.ExternalId).Concat(i.Rooms.Select(x=>x.ExternalId)).Concat(i.StorageLocations.Select(x=>x.ExternalId)).Concat(i.Assets.Select(x=>x.ExternalId)).ToList();if(ids.Any(string.IsNullOrWhiteSpace)||ids.Count!=ids.Distinct().Count()) errors.Add("Every record needs a unique externalId.");var propIds=i.Properties.Select(x=>x.ExternalId).ToHashSet();var roomIds=i.Rooms.Select(x=>x.ExternalId).ToHashSet();var locIds=i.StorageLocations.Select(x=>x.ExternalId).ToHashSet();if(i.Properties.Any(x=>string.IsNullOrWhiteSpace(x.Name))||i.Assets.Any(x=>string.IsNullOrWhiteSpace(x.Name)||string.IsNullOrWhiteSpace(x.Category))) errors.Add("Properties and assets require names; assets also require categories.");if(i.Rooms.Any(x=>!propIds.Contains(x.PropertyExternalId))||i.StorageLocations.Any(x=>!propIds.Contains(x.PropertyExternalId))||i.Assets.Any(x=>!propIds.Contains(x.PropertyExternalId))) errors.Add("Every room, location, and asset must reference an imported property.");if(i.Assets.Any(x=>x.RoomExternalId is not null&&!roomIds.Contains(x.RoomExternalId)||x.StorageLocationExternalId is not null&&!locIds.Contains(x.StorageLocationExternalId))) errors.Add("Assets reference an unknown room or storage location.");var existing=await AssetDtos(db,null,false);var duplicates=i.Assets.Where(a=>existing.Any(e=>Norm(e.Name)==Norm(a.Name)&&Norm(e.LocationPath)==Norm(LocationImportPath(a,i)))).Select(x=>x.ExternalId).ToList();return new(errors.Count==0,errors,i.Properties.Count,i.Rooms.Count,i.StorageLocations.Count,i.Assets.Count,duplicates); }
    static string? LocationImportPath(ImportAsset a, InventoryExport i)
    {
        if (a.StorageLocationExternalId is null) return a.RoomExternalId is null ? null : i.Rooms.SingleOrDefault(x => x.ExternalId == a.RoomExternalId)?.Name;
        var locations = i.StorageLocations.ToDictionary(x => x.ExternalId);
        string Build(string id) { var location = locations[id]; return location.ParentExternalId is null ? location.Name : $"{Build(location.ParentExternalId)} → {location.Name}"; }
        return locations.ContainsKey(a.StorageLocationExternalId) ? Build(a.StorageLocationExternalId) : null;
    }
    static string Norm(string? value)=>(value??"").Trim().ToLowerInvariant();
}
