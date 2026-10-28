using Microsoft.EntityFrameworkCore;
using OpenLethe.Data;

[Collection("postgres")]
public class AccountPersistenceTests(PostgresFixture db)
{
    [SkippableFact]
    public async Task JsonbDocument_RoundTripsUnchanged()
    {
        db.RequireDb();
        var doc = """{"nested":{"a":1},"list":[1,2,3]}""";

        Guid id;
        await using (var ctx = db.NewContext())
        {
            var acc = new Account { Id = Guid.NewGuid(), Username = $"rt_{Guid.NewGuid():N}", IngameId = Random.Shared.Next(), MdSaveInfo = doc };
            ctx.Accounts.Add(acc);
            await ctx.SaveChangesAsync();
            id = acc.Id;
        }

        await using (var ctx = db.NewContext())
        {
            var loaded = await ctx.Accounts.SingleAsync(a => a.Id == id);
            // jsonb normalizes whitespace but preserves structure/values; compare parsed.
            using var got = System.Text.Json.JsonDocument.Parse(loaded.MdSaveInfo);
            Assert.Equal(1, got.RootElement.GetProperty("nested").GetProperty("a").GetInt32());
            Assert.Equal(3, got.RootElement.GetProperty("list")[2].GetInt32());
        }
    }

    // jsonb hands back reordered, respaced text, so a handler re-serializing an unchanged
    // document never string-equals what it loaded. AppDbContext must not rewrite it: every
    // rewrite of a large document leaves a dead TOAST copy for autovacuum.
    [SkippableFact]
    public async Task SemanticallyUnchangedDocument_IsNotRewritten()
    {
        db.RequireDb();
        Guid id;
        await using (var ctx = db.NewContext())
        {
            var acc = new Account { Id = Guid.NewGuid(), Username = $"noop_{Guid.NewGuid():N}", IngameId = Random.Shared.Next(), MdSaveInfo = """{"zz":1,"a":[1,2]}""" };
            ctx.Accounts.Add(acc);
            await ctx.SaveChangesAsync();
            id = acc.Id;
        }

        await using (var ctx = db.NewContext())
        {
            var loaded = await ctx.Accounts.SingleAsync(a => a.Id == id);
            loaded.MdSaveInfo = """{"zz":1,"a":[1,2]}""";   // same content, our formatting
            Assert.Equal(0, await ctx.SaveChangesAsync());

            loaded.MdSaveInfo = """{"zz":2,"a":[1,2]}""";
            Assert.Equal(1, await ctx.SaveChangesAsync());
        }
    }
}
