namespace API.Migrations
{
    public partial class AddSkillSearchAliases
    {
        // Generated once from API/Data/SkillCatalog/skill-catalog-v2.csv. Adds the
        // one skill that is new in v2, unless the name is already in use, then
        // sets the search aliases by name. Rows that are not listed keep none.
        private const string AddCatalogV2Sql =
            """
INSERT INTO "Skills" ("Id", "Name", "NormalizedName", "Category", "NameTr", "NormalizedNameTr", "CreatedAt")
SELECT '84b5fff0-d5b1-529c-a17c-fa59e267ccd0'::uuid, 'Artificial Intelligence', 'artificial intelligence', 10, 'Yapay zeka', 'yapay zeka', TIMESTAMPTZ '2026-11-03T00:00:00Z'
WHERE NOT EXISTS (SELECT 1 FROM "Skills" WHERE "NormalizedName" = 'artificial intelligence')
ON CONFLICT DO NOTHING;

UPDATE "Skills" AS skill
SET "SearchAliases" = catalog.aliases
FROM (VALUES
    ('c', ARRAY['c dili']::text[]),
    ('c#', ARRAY['csharp', 'c sharp']::text[]),
    ('c++', ARRAY['cpp', 'c plus plus']::text[]),
    ('f#', ARRAY['fsharp']::text[]),
    ('go', ARRAY['golang']::text[]),
    ('javascript', ARRAY['js', 'ecmascript', 'es6']::text[]),
    ('python', ARRAY['py']::text[]),
    ('r', ARRAY['r dili']::text[]),
    ('typescript', ARRAY['ts']::text[]),
    ('css', ARRAY['css3']::text[]),
    ('html', ARRAY['html5']::text[]),
    ('material ui', ARRAY['mui']::text[]),
    ('next.js', ARRAY['nextjs']::text[]),
    ('nuxt', ARRAY['nuxtjs', 'nuxt.js']::text[]),
    ('react', ARRAY['reactjs', 'react.js']::text[]),
    ('react native', ARRAY['rn']::text[]),
    ('sass', ARRAY['scss']::text[]),
    ('tailwind css', ARRAY['tailwind']::text[]),
    ('vue.js', ARRAY['vue', 'vuejs']::text[]),
    ('web accessibility', ARRAY['a11y']::text[]),
    ('.net', ARRAY['dotnet', 'dot net', '.net core']::text[]),
    ('asp.net core', ARRAY['aspnet', 'asp.net']::text[]),
    ('entity framework core', ARRAY['efcore', 'ef core']::text[]),
    ('express', ARRAY['expressjs', 'express.js']::text[]),
    ('node.js', ARRAY['node', 'nodejs']::text[]),
    ('openapi', ARRAY['swagger']::text[]),
    ('rest api', ARRAY['rest', 'restful']::text[]),
    ('elasticsearch', ARRAY['elastic']::text[]),
    ('microsoft sql server', ARRAY['mssql', 'sql server']::text[]),
    ('mongodb', ARRAY['mongo']::text[]),
    ('postgresql', ARRAY['postgres', 'pg', 'psql']::text[]),
    ('bash', ARRAY['shell scripting', 'shell', 'sh', 'kabuk betikleme']::text[]),
    ('ci/cd', ARRAY['cicd', 'ci cd']::text[]),
    ('infrastructure as code', ARRAY['iac']::text[]),
    ('kubernetes', ARRAY['k8s', 'kube']::text[]),
    ('site reliability engineering', ARRAY['sre']::text[]),
    ('aws', ARRAY['amazon web services']::text[]),
    ('google cloud', ARRAY['gcp', 'google cloud platform']::text[]),
    ('microsoft azure', ARRAY['azure']::text[]),
    ('behavior-driven development', ARRAY['bdd']::text[]),
    ('end-to-end testing', ARRAY['e2e']::text[]),
    ('test-driven development', ARRAY['tdd']::text[]),
    ('apache kafka', ARRAY['kafka']::text[]),
    ('domain-driven design', ARRAY['ddd']::text[]),
    ('object-oriented programming', ARRAY['oop']::text[]),
    ('microsoft excel', ARRAY['excel']::text[]),
    ('power bi', ARRAY['powerbi']::text[]),
    ('visual studio code', ARRAY['vscode', 'vs code']::text[]),
    ('deep learning', ARRAY['dl']::text[]),
    ('artificial intelligence', ARRAY['ai']::text[]),
    ('machine learning', ARRAY['ml']::text[]),
    ('natural language processing', ARRAY['nlp']::text[]),
    ('search engine optimization', ARRAY['seo']::text[]),
    ('ui design', ARRAY['ui']::text[]),
    ('ux design', ARRAY['ux']::text[])
) AS catalog(name, aliases)
WHERE skill."NormalizedName" = catalog.name;
""";

        // Removes the v2 skill only while nothing references it.
        private const string RemoveCatalogV2Sql =
            """
DELETE FROM "Skills" AS skill
WHERE skill."Id" = '84b5fff0-d5b1-529c-a17c-fa59e267ccd0'::uuid
  AND NOT EXISTS (SELECT 1 FROM "ResumeSkills" AS item WHERE item."SkillId" = skill."Id")
  AND NOT EXISTS (SELECT 1 FROM "ProjectSkills" AS item WHERE item."SkillId" = skill."Id")
  AND NOT EXISTS (SELECT 1 FROM "JobPostingSkills" AS item WHERE item."SkillId" = skill."Id")
  AND NOT EXISTS (SELECT 1 FROM "KeywordMatches" AS item WHERE item."SkillId" = skill."Id")
  AND NOT EXISTS (SELECT 1 FROM "JobPostingParseDraftSkills" AS item WHERE item."ResolvedSkillId" = skill."Id");
""";
    }
}
