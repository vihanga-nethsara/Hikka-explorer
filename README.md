# Hikka Explorer
1. Run Database/schema.sql in SSMS (creates DB, tables, seeds 10 places).
2. Edit ConnectionStrings:Default in appsettings.json if your SQL Server instance differs.
3. `dotnet run`, then open the URL shown (https://localhost:xxxx).
4. Put photos in wwwroot/images and INSERT rows into AttractionImage (ImagePath like images/galle-fort.jpg).
5. Admin page: /admin.html (login in appsettings.json, section "Admin" - change the password).
