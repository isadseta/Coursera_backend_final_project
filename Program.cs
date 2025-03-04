using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net.Http.Json;
using Xunit;
using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Caching.Memory; // Added for validation attributes
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;


var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();

var app = builder.Build();
var key = Encoding.ASCII.GetBytes("your_secret_key_here");

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = "your_issuer",
        ValidAudience = "your_audience",
        IssuerSigningKey = new SymmetricSecurityKey(key)
    };
});

// Middleware to enforce standardized error handling
app.Use(async (context, next) =>
{
    try
    {
        await next.Invoke();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Unhandled exception: {ex.Message}");
        context.Response.StatusCode = 500;
        await context.Response.WriteAsJsonAsync(new { error = "An unexpected error occurred. Please try again later." });
    }
});

app.UseAuthentication();
app.UseAuthorization();

// Middleware for logging
app.Use(async (context, next) =>
{
    // Log incoming request
    Console.WriteLine($"HTTP {context.Request.Method} {context.Request.Path}");

    // Call the next middleware in the pipeline
    await next.Invoke();

    // Log outgoing response
    Console.WriteLine($"Response Status Code: {context.Response.StatusCode}");
});
// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

// Add CRUD endpoints for managing users
var users = new List<User>();

// Added caching to optimize performance for GET /users endpoint
var cache = new MemoryCache(new MemoryCacheOptions());

app.MapGet("/users", () =>
{
    try
    {
        if (!cache.TryGetValue("users", out List<User> cachedUsers))
        {
            cachedUsers = users;
            var cacheEntryOptions = new MemoryCacheEntryOptions()
                .SetSlidingExpiration(TimeSpan.FromMinutes(5));
            cache.Set("users", cachedUsers, cacheEntryOptions);
        }
        return Results.Ok(cachedUsers);
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message);
    }
});

app.MapGet("/users/{id}", (int id) =>
{
    try
    {
        var user = users.FirstOrDefault(u => u.Id == id);
        return user is not null ? Results.Ok(user) : Results.NotFound();
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message);
    }
});

app.MapPost("/users", (User user) =>
{
    try
    {
        // Validate user data
        var validationResults = new List<ValidationResult>();
        var validationContext = new ValidationContext(user);
        if (!Validator.TryValidateObject(user, validationContext, validationResults, true))
        {
            return Results.BadRequest(validationResults);
        }

        user.Id = users.Count > 0 ? users.Max(u => u.Id) + 1 : 1;
        users.Add(user);

        // Invalidate cache after adding a new user
        cache.Remove("users");

        return Results.Created($"/users/{user.Id}", user);
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message);
    }
});

app.MapPut("/users/{id}", (int id, User updatedUser) =>
{
    try
    {
        var user = users.FirstOrDefault(u => u.Id == id);
        if (user is null)
        {
            return Results.NotFound();
        }

        // Validate updated user data
        var validationResults = new List<ValidationResult>();
        var validationContext = new ValidationContext(updatedUser);
        if (!Validator.TryValidateObject(updatedUser, validationContext, validationResults, true))
        {
            return Results.BadRequest(validationResults);
        }

        user.Name = updatedUser.Name;
        user.Email = updatedUser.Email;

        // Invalidate cache after updating a user
        cache.Remove("users");

        return Results.NoContent();
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message);
    }
});

app.MapDelete("/users/{id}", (int id) =>
{
    try
    {
        var user = users.FirstOrDefault(u => u.Id == id);
        if (user is null)
        {
            return Results.NotFound();
        }

        users.Remove(user);

        // Invalidate cache after deleting a user
        cache.Remove("users");

        return Results.NoContent();
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message);
    }
});

app.Run();

public record User
{
    public int Id { get; set; }

    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string Name { get; set; }


// Added caching to optimize performance for GET /users endpoint
// Invalidate cache after adding, updating, or deleting a user
    [Required]
    [EmailAddress]
    public string Email { get; set; } 
}

// Added validation attributes to the User record
// Added validation logic in the POST and PUT endpoints
public class ProgramTest : IClassFixture<WebApplicationFactory<IStartup>>
{
    private readonly WebApplicationFactory<IStartup> _factory;

    public ProgramTest(WebApplicationFactory<IStartup> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetUsers_ReturnsOk()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/users");
        response.EnsureSuccessStatusCode();
        var users = await response.Content.ReadFromJsonAsync<List<User>>();
        Assert.NotNull(users);
    }

    [Fact]
    public async Task GetUserById_ReturnsNotFound_ForInvalidId()
    {
        var client = _factory.CreateClient();
        try
        {
            var response = await client.GetAsync("/users/999");
            Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
        }
        catch (Exception ex)
        {
            Assert.True(false, $"Exception thrown: {ex.Message}");
        }
    }

    [Fact]
    public async Task PostUser_CreatesUser()
    {
        var client = _factory.CreateClient();
        var newUser = new User { Name = "Test User", Email = "test@example.com" };
        var response = await client.PostAsJsonAsync("/users", newUser);
        response.EnsureSuccessStatusCode();
        var createdUser = await response.Content.ReadFromJsonAsync<User>();
        Assert.NotNull(createdUser);
        Assert.Equal("Test User", createdUser.Name);
        Assert.Equal("test@example.com", createdUser.Email);
    }

    [Fact]
    public async Task PutUser_UpdatesUser()
    {
        var client = _factory.CreateClient();
        var newUser = new User { Name = "Test User", Email = "test@example.com" };
        var postResponse = await client.PostAsJsonAsync("/users", newUser);
        postResponse.EnsureSuccessStatusCode();
        var createdUser = await postResponse.Content.ReadFromJsonAsync<User>();

        var updatedUser = new User { Name = "Updated User", Email = "updated@example.com" };
        var putResponse = await client.PutAsJsonAsync($"/users/{createdUser.Id}", updatedUser);
        putResponse.EnsureSuccessStatusCode();

        var getResponse = await client.GetAsync($"/users/{createdUser.Id}");
        getResponse.EnsureSuccessStatusCode();
        var fetchedUser = await getResponse.Content.ReadFromJsonAsync<User>();
        Assert.Equal("Updated User", fetchedUser.Name);
        Assert.Equal("updated@example.com", fetchedUser.Email);
    }

    [Fact]
    public async Task DeleteUser_DeletesUser()
    {
        var client = _factory.CreateClient();
        var newUser = new User { Name = "Test User", Email = "test@example.com" };
        var postResponse = await client.PostAsJsonAsync("/users", newUser);
        postResponse.EnsureSuccessStatusCode();
        var createdUser = await postResponse.Content.ReadFromJsonAsync<User>();

        var deleteResponse = await client.DeleteAsync($"/users/{createdUser.Id}");
        deleteResponse.EnsureSuccessStatusCode();

        try
        {
            var getResponse = await client.GetAsync($"/users/{createdUser.Id}");
            Assert.Equal(System.Net.HttpStatusCode.NotFound, getResponse.StatusCode);
        }
        catch (Exception ex)
        {
            Assert.True(false, $"Exception thrown: {ex.Message}");
        }
    }

    [Fact]
    public async Task PostUser_ValidatesInputFields()
    {
        var client = _factory.CreateClient();
        var invalidUser = new User { Name = "", Email = "invalid-email" };
        var response = await client.PostAsJsonAsync("/users", invalidUser);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetUsers_ReturnsEmptyList_WhenNoUsersExist()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/users");
        response.EnsureSuccessStatusCode();
        var users = await response.Content.ReadFromJsonAsync<List<User>>();
        Assert.Empty(users);
    }

    [Fact]
    public async Task GetUsers_ReturnsMultipleUsers()
    {
        var client = _factory.CreateClient();
        var user1 = new User { Name = "User One", Email = "user1@example.com" };
        var user2 = new User { Name = "User Two", Email = "user2@example.com" };

        await client.PostAsJsonAsync("/users", user1);
        await client.PostAsJsonAsync("/users", user2);

        var response = await client.GetAsync("/users");
        response.EnsureSuccessStatusCode();
        var users = await response.Content.ReadFromJsonAsync<List<User>>();
        Assert.Equal(2, users.Count);
    }

    [Fact]
    public async Task GetUsers_PerformanceTest()
    {
        var client = _factory.CreateClient();
        var usersToCreate = 1000;
        for (int i = 0; i < usersToCreate; i++)
        {
            var user = new User { Name = $"User {i}", Email = $"user{i}@example.com" };
            await client.PostAsJsonAsync("/users", user);
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var response = await client.GetAsync("/users");
        stopwatch.Stop();

        response.EnsureSuccessStatusCode();
        var users = await response.Content.ReadFromJsonAsync<List<User>>();
        Assert.Equal(usersToCreate, users.Count);
        Assert.True(stopwatch.ElapsedMilliseconds < 1000, "GET /users took too long to respond");
    }
}
