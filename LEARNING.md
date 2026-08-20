# Learning Experiments

Ten hands-on experiments to run against this project. Each links back to
the relevant [README.md](README.md) section. Run `docker compose up -d` and
`dotnet run` (from the repo root) first — see README §3.

Assume the API is at `http://localhost:5000` below (check your console for
the actual port `dotnet run` picks).

---

## Experiment 1 — Why does "iphone" match "Apple iPhone"?

Create a product:

```bash
curl -X POST http://localhost:5000/products -H "Content-Type: application/json" -d '{
  "name": "Apple iPhone", "description": "A phone", "category": "Smartphones",
  "brand": "Apple", "price": 90000, "rating": 4.5, "popularity": 80
}'
```

Search:

```bash
curl "http://localhost:5000/products/search?q=iphone"
```

**Observe:** it matches, even though you searched lowercase `iphone` and
the product name has `iPhone` capitalized. Then run:

```bash
curl "http://localhost:5000/learning/analyze?text=Apple%20iPhone"
```

You'll see the tokens are `["apple", "iphone"]` — both the field at index
time and your query at search time go through the same lowercase
tokenization, so they meet in the middle. See README [§9](README.md#9-text-analysis)–[§11](README.md#11-the-inverted-index).

---

## Experiment 2 — `match` vs `term` on the same value

Search for the brand using `match` semantics (via the free-text query,
which touches `name`/`description`, not `brand` — so first prove `brand`
truly isn't analyzed text):

```bash
# term query: exact, case-sensitive, against the keyword field
curl "http://localhost:5000/products/search?brand=Apple"    # matches
curl "http://localhost:5000/products/search?brand=apple"    # matches NOTHING — term query, no analysis
```

**Observe:** `brand=apple` (lowercase) returns zero results, because
`brand` is `keyword`, and the `term` query used against it in
`ProductService.SearchAsync` does not lowercase or tokenize the value —
`apple` != `Apple` as raw strings. Compare with:

```bash
curl "http://localhost:5000/products/search?q=apple"        # matches — q uses `match`, which analyzes
```

See README [§16](README.md#16-match-query) and [§17](README.md#17-term-query).

---

## Experiment 3 — Inspect the mapping, then the search response

```bash
curl http://localhost:9200/products-v1/_mapping | jq
curl "http://localhost:5000/products/search?q=iphone" | jq
```

**Observe:** in the mapping, `name`/`description` are `text`, `brand`/
`category` are `keyword`, `price`/`rating` are `double`, `popularity` is
`integer` — matching README [§7](README.md#7-mapping) exactly, because this project creates
that mapping explicitly at startup rather than letting OpenSearch guess it.

---

## Experiment 4 — Tokenize something surprising

```bash
curl "http://localhost:5000/learning/analyze?text=Apple%20iPhone%2015%20Pro%20Max"
```

**Observe:** `15` survives as its own token (numbers inside text fields are
still indexed as tokens — just not as *numeric* values usable in range
queries; that's only true for fields mapped as an actual numeric type).
Try analyzing something with punctuation, like `"iPhone's camera - 48MP!"`,
and see how the tokenizer splits on the apostrophe and punctuation.

---

## Experiment 5 — Watch refresh delay (or its absence)

This project calls an explicit `Indices.RefreshAsync` after seeding (see
README [§29](README.md#29-refresh)), so seeded data is searchable immediately. But a document
created through `POST /products` is *not* explicitly refreshed — try to
catch the ~1 second gap:

```bash
curl -X POST http://localhost:5000/products -H "Content-Type: application/json" -d '{
  "name": "Refresh Test Product", "description": "watch the timing",
  "category": "Test", "brand": "TestBrand", "price": 1, "rating": 1, "popularity": 1
}' && curl "http://localhost:5000/products/search?q=refresh"
```

**Observe:** depending on timing, the immediate search may return 0 hits
(the document isn't refreshed into a searchable segment yet) or 1 hit (if
the automatic ~1s refresh already ran). Run the search again a second
later — it will now reliably be there. See README [§29](README.md#29-refresh).

---

## Experiment 6 — Change a product, compare both stores

```bash
curl -X PUT http://localhost:5000/products/1 -H "Content-Type: application/json" -d '{
  "name": "iPhone 15 Pro Max (Updated)", "description": "Now with a new price",
  "category": "Smartphones", "brand": "Apple", "price": 99999, "rating": 4.9, "popularity": 99
}'

# PostgreSQL's view:
docker exec -it opensearch-lab-postgres psql -U postgres -d opensearchlab -c "select id, name, price, updated_at from products where id = 1;"

# OpenSearch's view:
curl http://localhost:9200/products-v1/_doc/1 | jq
```

**Observe:** both reflect the new price. Now stop the API process (Ctrl+C)
and repeat the `PUT` by directly editing the PostgreSQL row instead
(`UPDATE products SET price = 1 WHERE id = 1;`) — OpenSearch will NOT
change, because nothing told it to. This is the drift described in README
[§31](README.md#31-updates) and [§36](README.md#36-data-synchronization).

---

## Experiment 7 — Delete a product, observe both systems

```bash
curl -X DELETE http://localhost:5000/products/1
curl http://localhost:9200/products-v1/_doc/1   # -> "found": false
curl "http://localhost:5000/products/search?q=iphone"  # product 1 no longer appears
```

Then check the raw document count vs how many documents actually still
exist logically:

```bash
curl http://localhost:9200/products-v1/_count
curl "http://localhost:9200/products-v1/_stats/docs?pretty"
```

**Observe:** `_stats` distinguishes live doc count from `deleted` doc
count — deleted documents linger (as tombstones) until a segment merge
physically drops them. See README [§28](README.md#28-segments), [§30](README.md#30-segment-merging), [§32](README.md#32-deletes).

---

## Experiment 8 — Create a second index with a different mapping

```bash
curl -X PUT "http://localhost:9200/products-v2" -H "Content-Type: application/json" -d '{
  "mappings": {
    "properties": {
      "name": { "type": "text" },
      "description": { "type": "text" },
      "category": { "type": "text" },
      "brand": { "type": "keyword" },
      "price": { "type": "double" },
      "rating": { "type": "double" },
      "popularity": { "type": "integer" }
    }
  }
}'

curl -X POST "http://localhost:9200/_reindex" -H "Content-Type: application/json" -d '{
  "source": { "index": "products-v1" },
  "dest": { "index": "products-v2" }
}'

curl "http://localhost:9200/products-v2/_search?q=smartphones"
```

**Observe:** `category` is now `text` in `products-v2`, so a fuzzy/partial
match on category text works there in a way it never could against
`products-v1`'s `keyword` category. This is exactly why reindexing exists
— see README [§34](README.md#34-reindexing).

---

## Experiment 9 — Inspect `_score`

```bash
curl "http://localhost:5000/products/search?q=apple%20smartphone" | jq '.items[] | {name, score}'
```

**Observe:** products with "Apple" AND "smartphone"-like terms in the name
score higher than ones matching only one term, and matches in `name`
outscore matches only in `description` (because `name` is boosted `^2` in
`ProductService.SearchAsync`). See README [§21](README.md#21-relevance)–[§23](README.md#23-_score).

---

## Experiment 10 — Use `_explain` to understand a score

```bash
curl "http://localhost:5000/learning/explain/2?q=iphone" | jq
```

**Observe:** the nested `explanation` breaks the total score down into
per-clause contributions — look for `"description": "idf, computed as..."`
and `"description": "tf, computed as..."` deep inside the JSON tree; these
are the actual BM25 components described in README [§22](README.md#22-bm25). Compare the
explanation for a document that matches in `name` vs one that only matches
in `description`.

Then look at shards, just to see the concept made concrete on a
single-node cluster:

```bash
curl "http://localhost:9200/_cat/shards/products-v1?v"
```

**Observe:** one primary shard (`p`), 0 or more replicas (`r`), all
living on the one node in this project's `docker-compose.yml`. See README
[§25](README.md#25-shards)–[§26](README.md#26-replicas) for what changes in a real multi-node cluster.

---

## OpenSearch Dashboards

Open [http://localhost:5601](http://localhost:5601) → **Dev Tools** (left
sidebar) gives you a raw console to run any of the `curl` commands above as
plain OpenSearch DSL, e.g.:

```
GET products-v1/_mapping
GET products-v1/_search
{
  "query": { "match": { "name": "iphone" } }
}
```

Under **Discover**, you can create an index pattern for `products-v1*` and
browse indexed documents visually.
