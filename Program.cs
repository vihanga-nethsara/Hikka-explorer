using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

var app = WebApplication.CreateBuilder(args).Build();
var cs = app.Configuration.GetConnectionString("Default")!;

{   // First run: create the database from Database/schema.sql
    var master = new SqlConnectionStringBuilder(cs) { InitialCatalog = "master" }.ConnectionString;
    await using var cn0 = new SqlConnection(master);
    await cn0.OpenAsync();
    var exists = (int)(await new SqlCommand("SELECT COUNT(*) FROM sys.databases WHERE name='HikkaExplorer'", cn0).ExecuteScalarAsync())!;
    if (exists == 0)
    {
        var sql = await File.ReadAllTextAsync(Path.Combine(app.Environment.ContentRootPath, "Database", "schema.sql"));
        foreach (var batch in Regex.Split(sql, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
            if (!string.IsNullOrWhiteSpace(batch)) await new SqlCommand(batch, cn0).ExecuteNonQueryAsync();
    }
}
app.UseExceptionHandler(e => e.Run(async c => {          // NFR-12: no technical details shown to users
    c.Response.StatusCode = 500;
    await c.Response.WriteAsJsonAsync(new { message = "Something went wrong. Please try again later." });
}));
app.UseDefaultFiles(); app.UseStaticFiles();

// All DB access is server-side only (NFR-14) and fully parameterised.
async Task<List<Dictionary<string, object?>>> Q(string sql, params (string, object?)[] p)
{
    await using var cn = new SqlConnection(cs); await cn.OpenAsync();
    await using var cmd = new SqlCommand(sql, cn);
    foreach (var (k, v) in p) cmd.Parameters.AddWithValue(k, v ?? DBNull.Value);
    var list = new List<Dictionary<string, object?>>();
    await using var r = await cmd.ExecuteReaderAsync();
    while (await r.ReadAsync())
    {
        var d = new Dictionary<string, object?>();
        for (int i = 0; i < r.FieldCount; i++) d[r.GetName(i)] = r.IsDBNull(i) ? null : r.GetValue(i);
        list.Add(d);
    }
    return list;
}
string Tok(HttpContext c)
{
    if (!c.Request.Cookies.TryGetValue("hx", out var t) || !Guid.TryParse(t, out _))
    {
        t = Guid.NewGuid().ToString();
        c.Response.Cookies.Append("hx", t, new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Lax, MaxAge = TimeSpan.FromDays(7) });
    }
    return t;
}
IResult Msg(int code, string m) => Results.Json(new { message = m }, statusCode: code);
const string Ensure = "IF NOT EXISTS(SELECT 1 FROM Itinerary WHERE SessionToken=@t) INSERT INTO Itinerary(SessionToken) VALUES(@t);";

// Safe to run every start: adds new columns and renames the category, keeping existing data.
await Q(@"IF COL_LENGTH('Attraction','VisitDuration') IS NULL ALTER TABLE Attraction ADD VisitDuration NVARCHAR(50) NULL;
IF COL_LENGTH('Attraction','Facilities') IS NULL ALTER TABLE Attraction ADD Facilities NVARCHAR(300) NULL;
UPDATE Category SET Name=N'Heritage' WHERE Name=N'Historical';");

app.MapGet("/api/categories", async () => Results.Ok(await Q("SELECT CategoryId,Name,Description FROM Category ORDER BY Name")));

app.MapGet("/api/attractions", async (HttpContext c, string? search, int? categoryId, string? sort) => {
    if (c.Request.Query.ContainsKey("search") && string.IsNullOrWhiteSpace(search)) return Msg(400, "Please enter a place name or keyword to search.");
    if (search is { Length: > 100 }) return Msg(400, "Search text is too long (maximum 100 characters).");
    var s = string.IsNullOrWhiteSpace(search) ? null : "%" + search.Trim() + "%";
    return Results.Ok(await Q(@"SELECT a.AttractionId,a.Name,c.Name AS Category,a.CategoryId,a.Description,a.DistanceKm,
        (SELECT TOP 1 ImagePath FROM AttractionImage i WHERE i.AttractionId=a.AttractionId ORDER BY ImageId) AS Image
        FROM Attraction a JOIN Category c ON c.CategoryId=a.CategoryId
        WHERE a.IsAvailable=1 AND (@cat IS NULL OR a.CategoryId=@cat) AND (@s IS NULL OR a.Name LIKE @s OR a.Description LIKE @s OR c.Name LIKE @s)
        ORDER BY CASE WHEN @sort='name' THEN a.Name END, CASE WHEN @sort='far' THEN a.DistanceKm END DESC, a.DistanceKm", ("@cat", categoryId), ("@s", s), ("@sort", sort)));
});

app.MapGet("/api/attractions/{id:int}", async (int id) => {
    var a = (await Q(@"SELECT a.*,c.Name AS Category FROM Attraction a JOIN Category c ON c.CategoryId=a.CategoryId
        WHERE a.AttractionId=@id AND a.IsAvailable=1", ("@id", id))).FirstOrDefault();
    if (a is null) return Msg(404, "This attraction is not available.");
    a["Images"] = await Q("SELECT ImagePath,Caption FROM AttractionImage WHERE AttractionId=@id", ("@id", id));
    return Results.Ok(a);
});

app.MapGet("/api/itinerary", async (HttpContext c) => {
    var t = Tok(c);
    var rows = await Q(@"SELECT a.AttractionId,a.Name,a.DistanceKm,a.OpeningTimes,a.VisitDuration,ii.VisitOrder FROM ItineraryItem ii
        JOIN Itinerary i ON i.ItineraryId=ii.ItineraryId JOIN Attraction a ON a.AttractionId=ii.AttractionId
        WHERE i.SessionToken=@t ORDER BY ii.VisitOrder", ("@t", t));
    return Results.Json(rows);
});

app.MapPost("/api/itinerary/{id:int}", async (HttpContext c, int id) => {
    var t = Tok(c);
    if ((await Q("SELECT 1 AS x FROM Attraction WHERE AttractionId=@id AND IsAvailable=1", ("@id", id))).Count == 0)
        return Msg(400, "This attraction cannot be added to your plan.");
    await Q(Ensure, ("@t", t));
    if ((await Q(@"SELECT 1 AS x FROM ItineraryItem ii JOIN Itinerary i ON i.ItineraryId=ii.ItineraryId
        WHERE i.SessionToken=@t AND ii.AttractionId=@id", ("@t", t), ("@id", id))).Count > 0)
        return Msg(409, "This place is already in your plan.");
    await Q(@"DECLARE @i INT=(SELECT ItineraryId FROM Itinerary WHERE SessionToken=@t);
        INSERT INTO ItineraryItem(ItineraryId,AttractionId,VisitOrder)
        VALUES(@i,@id,ISNULL((SELECT MAX(VisitOrder) FROM ItineraryItem WHERE ItineraryId=@i),0)+1)", ("@t", t), ("@id", id));
    return Msg(200, "Added to your plan.");
});

app.MapDelete("/api/itinerary/{id:int}", async (HttpContext c, int id) => {
    await Q(@"DELETE ii FROM ItineraryItem ii JOIN Itinerary i ON i.ItineraryId=ii.ItineraryId
        WHERE i.SessionToken=@t AND ii.AttractionId=@id", ("@t", Tok(c)), ("@id", id));
    return Msg(200, "Removed from your plan.");
});

app.MapPut("/api/itinerary/{id:int}/move", async (HttpContext c, int id, string dir) => {
    if (dir != "up" && dir != "down") return Msg(400, "Invalid action.");
    string op = dir == "up" ? "<" : ">", ord = dir == "up" ? "DESC" : "ASC";   // fixed literals, not user text
    await Q($@"DECLARE @i INT=(SELECT ItineraryId FROM Itinerary WHERE SessionToken=@t);
        DECLARE @o INT=(SELECT VisitOrder FROM ItineraryItem WHERE ItineraryId=@i AND AttractionId=@id);
        DECLARE @n INT,@no INT;
        SELECT TOP 1 @n=AttractionId,@no=VisitOrder FROM ItineraryItem WHERE ItineraryId=@i AND VisitOrder {op} @o ORDER BY VisitOrder {ord};
        IF @n IS NOT NULL BEGIN
          UPDATE ItineraryItem SET VisitOrder=@no WHERE ItineraryId=@i AND AttractionId=@id;
          UPDATE ItineraryItem SET VisitOrder=@o WHERE ItineraryId=@i AND AttractionId=@n; END", ("@t", Tok(c)), ("@id", id));
    return Msg(200, "Plan updated.");
});
// ---------- Administrator ----------
var sessions = new HashSet<string>();
var adminUser = app.Configuration["Admin:Username"]; var adminPass = app.Configuration["Admin:Password"];
bool IsAdmin(HttpContext c) => c.Request.Cookies.TryGetValue("hxa", out var t) && t is not null && sessions.Contains(t);
string? N(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
string? Check(PlaceDto d) =>
    string.IsNullOrWhiteSpace(d.Name) || d.Name.Length > 100 ? "Name is required (maximum 100 characters)."
  : string.IsNullOrWhiteSpace(d.Description) || d.Description.Length > 1000 ? "Description is required (maximum 1000 characters)."
  : d.CategoryId <= 0 ? "Please choose a category."
  : d.DistanceKm is < 0 or > 25 ? "Distance must be between 0 and 25 km."
  : d.Latitude is < -90 or > 90 || d.Longitude is < -180 or > 180 ? "Latitude or longitude is out of range."
  : (d.Latitude is null) != (d.Longitude is null) ? "Enter both latitude and longitude, or neither."
  : (d.OpeningTimes?.Length > 200 || d.EntranceFee?.Length > 100 || d.TravelTips?.Length > 500 || d.VisitorInfo?.Length > 500
     || d.VisitDuration?.Length > 50 || d.Facilities?.Length > 300) ? "One of the text fields is too long."
  : null;
(string, object?)[] P(PlaceDto d) => new (string, object?)[] {
    ("@n", d.Name!.Trim()), ("@c", d.CategoryId), ("@d", d.Description!.Trim()), ("@la", d.Latitude), ("@lo", d.Longitude),
    ("@di", d.DistanceKm), ("@ot", N(d.OpeningTimes)), ("@ef", N(d.EntranceFee)), ("@tt", N(d.TravelTips)),
    ("@vi", N(d.VisitorInfo)), ("@vd", N(d.VisitDuration)), ("@fa", N(d.Facilities)), ("@av", d.IsAvailable), ("@img", N(d.ImagePath)) };

app.MapPost("/api/admin/login", (HttpContext c, LoginDto d) => {
    if (d.Username != adminUser || d.Password != adminPass) return Msg(401, "Incorrect username or password.");
    var t = Guid.NewGuid().ToString(); sessions.Add(t);
    c.Response.Cookies.Append("hxa", t, new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Strict });
    return Msg(200, "Logged in.");
});
app.MapPost("/api/admin/logout", (HttpContext c) => {
    if (c.Request.Cookies.TryGetValue("hxa", out var t) && t is not null) sessions.Remove(t);
    c.Response.Cookies.Delete("hxa"); return Msg(200, "Logged out.");
});
app.MapGet("/api/admin/attractions", async (HttpContext c) => !IsAdmin(c) ? Msg(401, "Please log in.") : Results.Json(await Q(
    @"SELECT a.*,(SELECT TOP 1 ImagePath FROM AttractionImage i WHERE i.AttractionId=a.AttractionId ORDER BY ImageId) AS ImagePath
      FROM Attraction a ORDER BY a.AttractionId")));
app.MapPost("/api/admin/attractions", async (HttpContext c, PlaceDto d) => {
    if (!IsAdmin(c)) return Msg(401, "Please log in.");
    if (Check(d) is { } err) return Msg(400, err);
    await Q(@"INSERT Attraction(Name,CategoryId,Description,Latitude,Longitude,DistanceKm,OpeningTimes,EntranceFee,TravelTips,VisitorInfo,VisitDuration,Facilities,IsAvailable)
        VALUES(@n,@c,@d,@la,@lo,@di,@ot,@ef,@tt,@vi,@vd,@fa,@av);
        DECLARE @id INT=SCOPE_IDENTITY();
        IF @img IS NOT NULL INSERT AttractionImage(AttractionId,ImagePath,Caption) VALUES(@id,@img,@n)", P(d));
    return Msg(200, "Place added.");
});
app.MapPut("/api/admin/attractions/{id:int}", async (HttpContext c, int id, PlaceDto d) => {
    if (!IsAdmin(c)) return Msg(401, "Please log in.");
    if (Check(d) is { } err) return Msg(400, err);
    await Q(@"UPDATE Attraction SET Name=@n,CategoryId=@c,Description=@d,Latitude=@la,Longitude=@lo,DistanceKm=@di,OpeningTimes=@ot,
        EntranceFee=@ef,TravelTips=@tt,VisitorInfo=@vi,VisitDuration=@vd,Facilities=@fa,IsAvailable=@av WHERE AttractionId=@id;
        DELETE FROM AttractionImage WHERE AttractionId=@id;
        IF @img IS NOT NULL INSERT AttractionImage(AttractionId,ImagePath,Caption) VALUES(@id,@img,@n)",
        P(d).Append(("@id", (object?)id)).ToArray());
    return Msg(200, "Place updated.");
});
app.MapDelete("/api/admin/attractions/{id:int}", async (HttpContext c, int id) => {
    if (!IsAdmin(c)) return Msg(401, "Please log in.");
    await Q("DELETE FROM ItineraryItem WHERE AttractionId=@id; DELETE FROM Attraction WHERE AttractionId=@id", ("@id", id));
    return Msg(200, "Place deleted.");
});

app.Run("http://localhost:5000");

record LoginDto(string Username, string Password);
record PlaceDto(string? Name, int CategoryId, string? Description, decimal? Latitude, decimal? Longitude, decimal? DistanceKm,
    string? OpeningTimes, string? EntranceFee, string? TravelTips, string? VisitorInfo, bool IsAvailable, string? ImagePath,
    string? VisitDuration, string? Facilities);