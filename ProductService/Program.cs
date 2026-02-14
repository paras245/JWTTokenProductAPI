using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ProductService.Data;
using ProductService.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Product API",
        Version = "v1"
    });

    // 🔐 Add JWT Authentication Definition
    options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Enter 'YOUR_TOKEN ONLY' below"
    });

    // 🔐 Require JWT globally
    options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            new string[] {}
        }
    });
});


var jwtSettings = builder.Configuration.GetSection("Jwt");
var key = Encoding.UTF8.GetBytes(jwtSettings["Key"]);

#region JWT Logic Start
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false;
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidAudience = builder.Configuration["Jwt:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(
        Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"])
    )
    };
    options.Events = new JwtBearerEvents
    {
        OnAuthenticationFailed = context =>
        {
            Console.WriteLine("Token failed: " + context.Exception.Message);
            return Task.CompletedTask;
        },
        OnTokenValidated = context =>
        {
            Console.WriteLine("Token validated successfully");
            return Task.CompletedTask;
        }
    };

});

builder.Services.AddAuthorization();

#endregion JWT Logic End

var app = builder.Build();
// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();


var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", () =>
{
    var forecast = Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetWeatherForecast")
.WithOpenApi().RequireAuthorization();



//Jwt Token Issuer
app.MapPost("/login", (UserLogin request, IConfiguration config) =>
{
    // Example hardcoded validation (replace with DB validation)
    if (request.UserName != "Paras" || request.Password != "123")
        return Results.Unauthorized();

    var claims = new[]
    {
        new Claim(ClaimTypes.Name, request.UserName),
        new Claim(ClaimTypes.Role, "Admin")
    };

    var key = new SymmetricSecurityKey(
        Encoding.UTF8.GetBytes(config["Jwt:Key"])
    );

    var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

    var token = new JwtSecurityToken(
        issuer: config["Jwt:Issuer"],
        audience: config["Jwt:Audience"],
        claims: claims,
        expires: DateTime.UtcNow.AddMinutes(30),
        signingCredentials: creds
    );

    var jwt = new JwtSecurityTokenHandler().WriteToken(token);

    return Results.Ok(new
    {
        token = jwt
    });
});


var productGroup = app.MapGroup("/api/products")
                      .WithTags("Products")
                      .RequireAuthorization();

productGroup.MapGet("/", async (ApplicationDbContext db) =>
{
    try
    {
        var products = await db.ProductsMicroservice
                               .AsNoTracking()
                               .ToListAsync();

        if (!products.Any())
            return Results.NoContent(); // 204

        return Results.Ok(products); // 200
    }
    catch (Exception ex)
    {
        return Results.Problem(
            title: "An error occurred while retrieving products.",
            detail: ex.Message,
            statusCode: StatusCodes.Status500InternalServerError);
    }
})
.Produces<List<Product>>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status204NoContent)
.Produces(StatusCodes.Status500InternalServerError)
.WithDescription("Retrieves all products.");


productGroup.MapGet("/{id:int}", async (int id, ApplicationDbContext db) =>
{
    try
    {
        var product = await db.ProductsMicroservice
                              .AsNoTracking()
                              .FirstOrDefaultAsync(p => p.Id == id);

        if (product is null)
            return Results.NotFound(new { message = "Product not found." });

        return Results.Ok(product);
    }
    catch (Exception ex)
    {
        return Results.Problem(
            title: "Error retrieving product.",
            detail: ex.Message,
            statusCode: StatusCodes.Status500InternalServerError);
    }
})
.Produces<Product>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound)
.Produces(StatusCodes.Status500InternalServerError)
.WithDescription("Retrieves a product by its ID.");

productGroup.MapPost("/", async (Product product, ApplicationDbContext db) =>
{
    try
    {
        if (product == null)
            return Results.BadRequest(new { message = "Invalid product data." });

        await db.ProductsMicroservice.AddAsync(product);
        await db.SaveChangesAsync();

        return Results.Created($"/api/products/{product.Id}", product);
    }
    catch (Exception ex)
    {
        return Results.Problem(
            title: "Error creating product.",
            detail: ex.Message,
            statusCode: StatusCodes.Status500InternalServerError);
    }
})
.Produces<Product>(StatusCodes.Status201Created)
.Produces(StatusCodes.Status400BadRequest)
.Produces(StatusCodes.Status500InternalServerError)
.WithDescription("Creates a new product.");

productGroup.MapPut("/{id:int}", async (int id, Product updatedProduct, ApplicationDbContext db) =>
{
    try
    {
        var product = await db.ProductsMicroservice.FindAsync(id);

        if (product is null)
            return Results.NotFound(new { message = "Product not found." });

        product.Name = updatedProduct.Name;
        product.Description = updatedProduct.Description;
        product.Price = updatedProduct.Price;

        await db.SaveChangesAsync();

        return Results.Ok(new { message = "Product updated successfully." });
    }
    catch (Exception ex)
    {
        return Results.Problem(
            title: "Error updating product.",
            detail: ex.Message,
            statusCode: StatusCodes.Status500InternalServerError);
    }
})
.Produces(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound)
.Produces(StatusCodes.Status500InternalServerError)
.WithDescription("Updates an existing product.");

productGroup.MapDelete("/{id:int}", async (int id, ApplicationDbContext db) =>
{
    try
    {
        var product = await db.ProductsMicroservice.FindAsync(id);

        if (product is null)
            return Results.NotFound(new { message = "Product not found." });

        db.ProductsMicroservice.Remove(product);
        await db.SaveChangesAsync();

        return Results.Ok(new { message = "Product deleted successfully." });
    }
    catch (Exception ex)
    {
        return Results.Problem(
            title: "Error deleting product.",
            detail: ex.Message,
            statusCode: StatusCodes.Status500InternalServerError);
    }
})
.Produces(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound)
.Produces(StatusCodes.Status500InternalServerError)
.WithDescription("Deletes a product by ID.");


#region start old Code

//// GET ALL PRODUCTS
//app.MapGet("/products", async (ApplicationDbContext db) =>
//{
//    var products = await db.ProductsMicroservice.ToListAsync();
//    return Results.Ok(products);
//}).RequireAuthorization();

//// GET PRODUCT BY ID
//app.MapGet("/products/{id:int}", async (int id, ApplicationDbContext db) =>
//{
//    var product = await db.ProductsMicroservice.FindAsync(id);

//    if (product is null)
//        return Results.NotFound();

//    return Results.Ok(product);
//}).RequireAuthorization();

//// CREATE PRODUCT
//app.MapPost("/products", async (Product product, ApplicationDbContext db) =>
//{
//    db.ProductsMicroservice.Add(product);
//    await db.SaveChangesAsync();

//    return Results.Created($"/products/{product.Id}", product);
//}).RequireAuthorization();

//// UPDATE PRODUCT
//app.MapPut("/products/{id:int}", async (int id, Product updatedProduct, ApplicationDbContext db) =>
//{
//    var product = await db.ProductsMicroservice.FindAsync(id);

//    if (product is null)
//        return Results.NotFound();

//    product.Name = updatedProduct.Name;
//    product.Description = updatedProduct.Description;
//    product.Price = updatedProduct.Price;

//    await db.SaveChangesAsync();

//    return Results.NoContent();
//}).RequireAuthorization();

//// DELETE PRODUCT
//app.MapDelete("/products/{id:int}", async (int id, ApplicationDbContext db) =>
//{
//    var product = await db.ProductsMicroservice.FindAsync(id);

//    if (product is null)
//        return Results.NotFound();

//    db.ProductsMicroservice.Remove(product);
//    await db.SaveChangesAsync();

//    return Results.NoContent();
//}).RequireAuthorization();

#endregion end old code

app.Run();

internal record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
