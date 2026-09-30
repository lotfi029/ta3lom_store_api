"""Step 1: read column comments from the DDL and the catalogue of a database built from the schema.
Usage: python3 gen_dictionary_step1.py <001_store_schema.sql> <database> <out.json>
Connection: the standard libpq variables (PGHOST, PGPORT, PGUSER, PGPASSWORD)."""
import re,subprocess,json,sys
ddl_path, database, out_path = sys.argv[1], sys.argv[2], sys.argv[3]
ddl=open(ddl_path,encoding='utf-8').read()
# column comments from DDL
notes={}
cur=None
for line in ddl.splitlines():
    m=re.match(r'CREATE TABLE (\w+)',line)
    if m: cur=m.group(1); continue
    if cur and line.startswith(');'): cur=None; continue
    if cur:
        m=re.match(r'\s+(\w+)\s+[^-]*--\s*(.+)$',line)
        if m: notes[(cur,m.group(1))]=m.group(2).strip()
def sql(q):
    r=subprocess.run(['psql','-d',database,'-At','-F','\t','-c',q],capture_output=True,text=True)
    if r.returncode: raise SystemExit(r.stderr)
    return r.stdout.strip().splitlines()
cols=sql("""select table_name,column_name,
 case when data_type='USER-DEFINED' then udt_name when data_type='ARRAY' then udt_name when character_maximum_length is not null then data_type||'('||character_maximum_length||')' when data_type='numeric' then 'numeric('||numeric_precision||','||numeric_scale||')' else data_type end,
 is_nullable, coalesce(column_default,''), ordinal_position
 from information_schema.columns where table_schema='store' and table_name in (select table_name from information_schema.tables where table_schema='store' and table_type='BASE TABLE' and table_name <> '__ef_migrations_history') order by table_name, ordinal_position""")
fks=sql("""select tc.table_name,kcu.column_name,ccu.table_name from information_schema.table_constraints tc join information_schema.key_column_usage kcu on tc.constraint_name=kcu.constraint_name and tc.table_schema=kcu.table_schema join information_schema.constraint_column_usage ccu on ccu.constraint_name=tc.constraint_name and ccu.table_schema=tc.table_schema where tc.constraint_type='FOREIGN KEY' and tc.table_schema='store'""")
pks=sql("""select tc.table_name,kcu.column_name from information_schema.table_constraints tc join information_schema.key_column_usage kcu on tc.constraint_name=kcu.constraint_name and tc.table_schema=kcu.table_schema where tc.constraint_type='PRIMARY KEY' and tc.table_schema='store'""")
uqs=sql("""select c.relname,a.attname from pg_index ix join pg_class c on c.oid=ix.indrelid join pg_namespace n on n.oid=c.relnamespace join pg_attribute a on a.attrelid=ix.indrelid and a.attnum=any(ix.indkey) where n.nspname='store' and ix.indisunique and not ix.indisprimary and ix.indpred is null""")   # UNIQUE constraints and non-partial unique indexes
fk={(a,b):c for a,b,c in (x.split('\t') for x in fks)}
pk={tuple(x.split('\t')) for x in pks}
uq={tuple(x.split('\t')) for x in uqs}
tables={}
for r in cols:
    t,c,ty,n,d,_=r.split('\t')
    tables.setdefault(t,[]).append((c,ty,n,d))
json.dump({'tables':{t:[list(x)+[ 'PK' if (t,x[0]) in pk else '', 'FK→'+fk[(t,x[0])] if (t,x[0]) in fk else '', 'UQ' if (t,x[0]) in uq else '', notes.get((t,x[0]),'')] for x in v] for t,v in tables.items()}},open(out_path,'w',encoding='utf-8'),ensure_ascii=False)
print(len(tables))
