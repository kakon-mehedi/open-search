using Microsoft.EntityFrameworkCore;
using OpenSearch.Client;
using OpenSearchLearningLab.Data;
using OpenSearchLearningLab.Models;

namespace OpenSearchLearningLab.OpenSearch;

// A small, realistic set of products so the search examples in README.md
// and LEARNING.md return interesting results out of the box, without you
// having to POST 15 products by hand first. Runs once, only when the
// products table is empty.
public static class SeedData
{
    public static async Task SeedIfEmptyAsync(AppDbContext db, IOpenSearchClient openSearch)
    {
        if (await db.Products.AnyAsync())
        {
            return;
        }

        var now = DateTime.UtcNow;
        var products = new List<Product>
        {
            New("iPhone 15 Pro Max", "Apple flagship smartphone with titanium design", "Smartphones", "Apple", 120000, 4.8, 95, now),
            New("iPhone 15", "Apple smartphone with A16 Bionic chip", "Smartphones", "Apple", 85000, 4.6, 90, now),
            New("Samsung Galaxy S24", "Compact Android flagship with AI features", "Smartphones", "Samsung", 75000, 4.5, 80, now),
            New("Samsung Galaxy S24 Ultra", "Android flagship with S Pen and 200MP camera", "Smartphones", "Samsung", 130000, 4.7, 88, now),
            New("Google Pixel 9", "Android phone with Tensor chip and pure Android", "Smartphones", "Google", 70000, 4.4, 70, now),
            New("MacBook Pro", "Apple laptop with M3 chip for professionals", "Laptops", "Apple", 220000, 4.9, 92, now),
            New("MacBook Air", "Thin and light Apple laptop with M2 chip", "Laptops", "Apple", 130000, 4.7, 85, now),
            New("Dell XPS 13", "Compact Windows ultrabook with InfinityEdge display", "Laptops", "Dell", 110000, 4.3, 60, now),
            New("Lenovo ThinkPad X1", "Business laptop known for durability and keyboard", "Laptops", "Lenovo", 140000, 4.5, 55, now),
            New("Sony WH-1000XM5", "Noise-cancelling over-ear wireless headphones", "Audio", "Sony", 35000, 4.8, 78, now),
            New("AirPods Pro", "Apple noise-cancelling wireless earbuds", "Audio", "Apple", 25000, 4.6, 89, now),
            New("Samsung Galaxy Buds2 Pro", "Samsung noise-cancelling wireless earbuds", "Audio", "Samsung", 18000, 4.2, 50, now),
            New("iPad Pro", "Apple tablet with M4 chip and Liquid Retina display", "Tablets", "Apple", 115000, 4.7, 75, now),
            New("Samsung Galaxy Tab S9", "Android tablet with AMOLED display and S Pen", "Tablets", "Samsung", 90000, 4.3, 45, now),
            New("Apple Watch Series 9", "Apple smartwatch with health tracking", "Wearables", "Apple", 45000, 4.6, 82, now),
        };

        db.Products.AddRange(products);
        await db.SaveChangesAsync();

        foreach (var product in products)
        {
            var document = ProductDocumentMapper.ToDocument(product);
            await openSearch.IndexAsync(document, i => i
                .Index(OpenSearchIndexSetup.IndexName)
                .Id(document.Id));
        }

        // Force an immediate refresh so the seed data is searchable right
        // away instead of waiting for the next automatic refresh cycle
        // (default: every 1s). See README "Refresh" — this call is the
        // manual version of what normally happens on a timer.
        await openSearch.Indices.RefreshAsync(OpenSearchIndexSetup.IndexName);
    }

    private static Product New(
        string name, string description, string category, string brand,
        decimal price, double rating, int popularity, DateTime now) => new()
    {
        Name = name,
        Description = description,
        Category = category,
        Brand = brand,
        Price = price,
        Rating = rating,
        Popularity = popularity,
        CreatedAt = now,
        UpdatedAt = now
    };
}
