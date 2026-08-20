using Microsoft.EntityFrameworkCore;
using OpenSearch.Client;
using OpenSearchLearningLab.Data;
using OpenSearchLearningLab.Models;
using OpenSearchLearningLab.OpenSearch;

namespace OpenSearchLearningLab.Services;

// This class is intentionally the only place where PostgreSQL and
// OpenSearch are touched together. Everything here follows the lifecycle
// diagram in the README:
//
//   PostgreSQL write  →  convert to ProductDocument  →  OpenSearch write
//
// There is no message queue, no outbox, no background worker. The two
// writes happen one after another, in the same request. That is a real
// limitation (if the process crashes between them, the two stores drift)
// and the README calls this out explicitly — the point of this project is
// to see the mechanics of OpenSearch, not to solve dual-write consistency.
public class ProductService
{
    private readonly AppDbContext _db;
    private readonly IOpenSearchClient _openSearch;
    private readonly ILogger<ProductService> _logger;

    public ProductService(AppDbContext db, IOpenSearchClient openSearch, ILogger<ProductService> logger)
    {
        _db = db;
        _openSearch = openSearch;
        _logger = logger;
    }

    public async Task<Product> CreateAsync(CreateProductRequest request)
    {
        // Step 1: PostgreSQL is the source of truth. The product does not
        // exist anywhere until this row is committed.
        var product = new Product
        {
            Name = request.Name,
            Description = request.Description,
            Category = request.Category,
            Brand = request.Brand,
            Price = request.Price,
            Rating = request.Rating,
            Popularity = request.Popularity,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _db.Products.Add(product);
        await _db.SaveChangesAsync();

        // Step 2: convert the row into the separate search document shape.
        var document = ProductDocumentMapper.ToDocument(product);

        // Step 3: hand the document to OpenSearch. This call returns once
        // OpenSearch has accepted and durably logged the write — it does
        // NOT wait for the document to become searchable. See the README
        // section "Refresh" for why a document indexed here may not show
        // up in a search that runs immediately after.
        var indexResponse = await _openSearch.IndexAsync(document, i => i
            .Index(OpenSearchIndexSetup.IndexName)
            .Id(document.Id));

        _logger.LogInformation(
            "Indexed product {ProductId} into {Index} (result: {Result})",
            document.Id, OpenSearchIndexSetup.IndexName, indexResponse.Result);

        return product;
    }

    public async Task<Product?> UpdateAsync(int id, UpdateProductRequest request)
    {
        var product = await _db.Products.FindAsync(id);
        if (product is null)
        {
            return null;
        }

        // Step 1: update PostgreSQL first — it remains the source of truth.
        product.Name = request.Name;
        product.Description = request.Description;
        product.Category = request.Category;
        product.Brand = request.Brand;
        product.Price = request.Price;
        product.Rating = request.Rating;
        product.Popularity = request.Popularity;
        product.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        // Step 2/3: rebuild the document from scratch and index it again
        // under the SAME id. This is a full replacement, not a partial
        // patch: OpenSearch throws away the previous document body for
        // this id and stores the new one. Internally this is not an
        // in-place mutation either — Lucene segments are immutable, so
        // this is really "mark the old version deleted, write a new one".
        // See the README sections "Updates" and "Deletes" for the detail.
        var document = ProductDocumentMapper.ToDocument(product);
        var indexResponse = await _openSearch.IndexAsync(document, i => i
            .Index(OpenSearchIndexSetup.IndexName)
            .Id(document.Id));

        _logger.LogInformation(
            "Re-indexed product {ProductId} into {Index} (result: {Result})",
            document.Id, OpenSearchIndexSetup.IndexName, indexResponse.Result);

        return product;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var product = await _db.Products.FindAsync(id);
        if (product is null)
        {
            return false;
        }

        // Step 1: delete from PostgreSQL.
        _db.Products.Remove(product);
        await _db.SaveChangesAsync();

        // Step 2: delete the corresponding OpenSearch document. Like the
        // update above, this does not free disk space immediately — the
        // document is marked with a deletion tombstone in its segment and
        // physically removed only when that segment is later merged. See
        // the README section "Deletes" for why.
        await _openSearch.DeleteAsync<ProductDocument>(id.ToString(), d => d
            .Index(OpenSearchIndexSetup.IndexName));

        return true;
    }

    // A single search entrypoint that builds one bool query out of
    // whichever filters were supplied. See the README "Query DSL" section
    // for what each piece (match / term / range / bool) means individually
    // — this method is where they get combined for the /products/search
    // endpoint.
    public async Task<SearchResponse> SearchAsync(
        string? q, string? brand, decimal? minPrice, decimal? maxPrice, double? minRating)
    {
        // Built up front as a plain list, then handed to .Filter(...) as an
        // array below — only the filters the caller actually asked for are
        // included, each one a separate `term` or `range` clause.
        var filters = new List<Func<QueryContainerDescriptor<ProductDocument>, QueryContainer>>();

        if (!string.IsNullOrWhiteSpace(brand))
        {
            filters.Add(f => f.Term(t => t.Field(x => x.Brand).Value(brand)));
        }
        if (minPrice.HasValue || maxPrice.HasValue)
        {
            filters.Add(f => f.Range(r =>
            {
                var range = r.Field(x => x.Price);
                if (minPrice.HasValue) range = range.GreaterThanOrEquals((double)minPrice.Value);
                if (maxPrice.HasValue) range = range.LessThanOrEquals((double)maxPrice.Value);
                return range;
            }));
        }
        if (minRating.HasValue)
        {
            filters.Add(f => f.Range(r => r.Field(x => x.Rating).GreaterThanOrEquals(minRating.Value)));
        }

        var response = await _openSearch.SearchAsync<ProductDocument>(s => s
            .Index(OpenSearchIndexSetup.IndexName)
            .Query(query => query
                .Bool(b => b
                    // must: affects relevance score, and the document must
                    // match. This is the analyzed full-text part of the
                    // search — only applied when the caller passed a `q`.
                    .Must(mu => string.IsNullOrWhiteSpace(q)
                        ? mu.MatchAll()
                        // multi_match runs the SAME analyzed query text against several
                        // fields and keeps the best-scoring match. Name is boosted 2x so a
                        // hit in the product name outweighs the same term merely appearing
                        // in the description.
                        : mu.MultiMatch(m => m
                            .Query(q)
                            .Fields(f => f
                                .Field(x => x.Name, boost: 2.0)
                                .Field(x => x.Description))))
                    // filter: must match, but runs outside of scoring and
                    // is cacheable. Exact-value and range constraints
                    // belong here, not in `must`, because "is this brand
                    // Apple" shouldn't make a document MORE relevant, only
                    // eligible.
                    .Filter(filters.ToArray())
                )));

        _logger.LogInformation(
            "Search against {Index} returned {Total} hits", OpenSearchIndexSetup.IndexName, response.Total);

        return new SearchResponse
        {
            Total = response.Total,
            Items = response.Hits.Select(h => new SearchResultItem
            {
                Id = h.Source.Id,
                Name = h.Source.Name,
                Brand = h.Source.Brand,
                Price = h.Source.Price,
                Rating = h.Source.Rating,
                Score = h.Score
            }).ToList()
        };
    }
}
