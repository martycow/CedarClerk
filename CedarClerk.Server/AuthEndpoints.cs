using System.Security.Claims;
using CedarClerk.Server.Data;
using Microsoft.AspNetCore.Identity;

namespace CedarClerk.Server;

public static class AuthEndpoints
{
    public record RegisterRequest(string Email, string Password, string InviteCode);
    public record LoginRequest(string Email, string Password);

    public static void MapAuthEndpoints(this WebApplication app)
    {
        var groupBuilder = app.MapGroup("/api/auth");

        groupBuilder.MapPost("/register", async (RegisterRequest req, UserManager<ApplicationUser> users, IConfiguration cfg) =>
        {
            var invite = cfg["Cedar:InviteCode"];
            if (string.IsNullOrEmpty(invite) || req.InviteCode != invite)
                return Results.BadRequest(new { error = "Invalid invite code" });

            var user = new ApplicationUser { UserName = req.Email, Email = req.Email };
            var result = await users.CreateAsync(user, req.Password);
            return result.Succeeded
                ? Results.Ok(new { message = "Registered" })
                : Results.BadRequest(new { errors = result.Errors.Select(e => e.Description) });
        });

        groupBuilder.MapPost("/login", async (LoginRequest req, SignInManager<ApplicationUser> signIn, UserManager<ApplicationUser> users) =>
        {
            var user = await users.FindByEmailAsync(req.Email);
            if (user is null) 
                return Results.Unauthorized();
            
            var result = await signIn.PasswordSignInAsync(user, req.Password, isPersistent: true, lockoutOnFailure: true);
            return result.Succeeded 
                ? Results.Ok(new { message = "Logged in" }) 
                : Results.Unauthorized();
        });

        groupBuilder.MapPost("/logout", async (SignInManager<ApplicationUser> signIn) =>
        {
            await signIn.SignOutAsync();
            return Results.Ok();
        }).RequireAuthorization();

        groupBuilder.MapGet("/me", (ClaimsPrincipal user) =>
            Results.Ok(new { email = user.FindFirstValue(ClaimTypes.Email) ?? user.Identity!.Name }))
        .RequireAuthorization();
    }
}