using OpenSearchLearningLab.Models;

namespace OpenSearchLearningLab.OpenSearch;

// The explicit "convert Product to OpenSearch ProductDocument" step from the
// lifecycle diagram in the README. It is a plain, boring function on
// purpose — the interesting behavior happens after this, inside OpenSearch
// itself (analysis, tokenization, inverted index updates), not here.
public static class ProductDocumentMapper
{
    public static ProductDocument ToDocument(Product product)
    {
        return new ProductDocument
        {
            Id = product.Id.ToString(),
            Name = product.Name,
            Description = product.Description,
            Category = product.Category,
            Brand = product.Brand,
            Price = product.Price,
            Rating = product.Rating,
            Popularity = product.Popularity
        };
    }
}
