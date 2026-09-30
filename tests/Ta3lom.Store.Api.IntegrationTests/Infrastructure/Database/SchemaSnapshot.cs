using System.Text.RegularExpressions;

namespace Ta3lom.Store.Api.IntegrationTests.Infrastructure.Database;

/// Everything in schema `store` that the parity test compares, as sorted lines (one object per line):
/// tables and views, columns (type, not-null, default, identity, generated expression), PK/FK/CHECK
/// constraints by name and definition, indexes by name and definition (a UNIQUE constraint and a unique
/// index with the same name and columns compare equal), view and function definitions, sequences and
/// extensions. Column order is not compared. Bodies are compared with whitespace collapsed.
public static partial class SchemaSnapshot
{
    private const string Sql = """
        SET search_path = store, public;
        SELECT 'table ' || c.relname || ' ' || c.relkind::text FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
         WHERE n.nspname = 'store' AND c.relkind IN ('r','v') AND c.relname <> '__ef_migrations_history'
        UNION ALL
        SELECT 'column ' || c.relname || '.' || a.attname || ' ' || format_type(a.atttypid, a.atttypmod)
               || ' notnull=' || a.attnotnull || ' default=' || coalesce(pg_get_expr(d.adbin, d.adrelid), '')
               || ' identity=' || a.attidentity::text || ' generated=' || a.attgenerated::text
          FROM pg_attribute a JOIN pg_class c ON c.oid = a.attrelid JOIN pg_namespace n ON n.oid = c.relnamespace
          LEFT JOIN pg_attrdef d ON d.adrelid = a.attrelid AND d.adnum = a.attnum
         WHERE n.nspname = 'store' AND c.relkind IN ('r','v') AND c.relname <> '__ef_migrations_history'
           AND a.attnum > 0 AND NOT a.attisdropped
        UNION ALL
        SELECT 'constraint ' || c.relname || '.' || co.conname || ' ' || pg_get_constraintdef(co.oid)
          FROM pg_constraint co JOIN pg_class c ON c.oid = co.conrelid JOIN pg_namespace n ON n.oid = c.relnamespace
         WHERE n.nspname = 'store' AND co.contype IN ('p','f','c') AND c.relname <> '__ef_migrations_history'
        UNION ALL
        SELECT 'index ' || tablename || '.' || indexname || ' ' || indexdef
          FROM pg_indexes WHERE schemaname = 'store' AND tablename <> '__ef_migrations_history'
        UNION ALL
        SELECT 'view ' || viewname || ' ' || definition FROM pg_views WHERE schemaname = 'store'
        UNION ALL
        SELECT 'function ' || p.proname || '(' || pg_get_function_arguments(p.oid) || ') ' || pg_get_function_result(p.oid)
               || ' volatility=' || p.provolatile::text || ' parallel=' || p.proparallel::text || ' ' || p.prosrc
          FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace
         WHERE n.nspname = 'store' AND NOT EXISTS (SELECT 1 FROM pg_depend d WHERE d.objid = p.oid AND d.deptype = 'e')
        UNION ALL
        SELECT 'sequence ' || s.relname || ' ' || format_type(q.seqtypid, NULL) || ' start=' || q.seqstart
               || ' increment=' || q.seqincrement || ' min=' || q.seqmin || ' max=' || q.seqmax || ' cycle=' || q.seqcycle
          FROM pg_sequence q JOIN pg_class s ON s.oid = q.seqrelid JOIN pg_namespace n ON n.oid = s.relnamespace
         WHERE n.nspname = 'store' AND NOT EXISTS (SELECT 1 FROM pg_depend d WHERE d.objid = s.oid AND d.deptype IN ('a','i'))
        UNION ALL
        SELECT 'extension ' || e.extname || ' schema=' || n.nspname
          FROM pg_extension e JOIN pg_namespace n ON n.oid = e.extnamespace WHERE e.extname <> 'plpgsql'
        """;

    public static async Task<IReadOnlyList<string>> TakeAsync(string connectionString)
    {
        var results = await SqlRunner.QueryAllAsync(connectionString, Sql);
        return [.. results.Single().Select(l => Whitespace().Replace(l, " ").Trim()).Order(StringComparer.Ordinal)];
    }

    /// Lines only on one side, grouped by object kind, for a readable failure message.
    public static string Diff(IReadOnlyList<string> expected, IReadOnlyList<string> actual)
    {
        var missing = expected.Except(actual, StringComparer.Ordinal).Select(l => "  - " + l);
        var extra = actual.Except(expected, StringComparer.Ordinal).Select(l => "  + " + l);
        return string.Join('\n', ["In the reference DDL but not in the migrations (-), or the other way round (+):", .. missing, .. extra]);
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
