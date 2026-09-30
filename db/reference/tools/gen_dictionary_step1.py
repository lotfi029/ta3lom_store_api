import re,subprocess,json
ddl=open('/home/claude/sd/database/store/001_store_schema.sql').read()
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
    return subprocess.run(['psql','-h','/tmp','-p','5433','-U','postgres','-At','-F','\t','-c',q],capture_output=True,text=True).stdout.strip().splitlines()
cols=sql("""select table_name,column_name,
 case when data_type='USER-DEFINED' then udt_name when data_type='ARRAY' then udt_name when character_maximum_length is not null then data_type||'('||character_maximum_length||')' when data_type='numeric' then 'numeric('||numeric_precision||','||numeric_scale||')' else data_type end,
 is_nullable, coalesce(column_default,''), ordinal_position
 from information_schema.columns where table_schema='store' and table_name in (select table_name from information_schema.tables where table_schema='store' and table_type='BASE TABLE') order by table_name, ordinal_position""")
fks=sql("""select tc.table_name,kcu.column_name,ccu.table_name from information_schema.table_constraints tc join information_schema.key_column_usage kcu on tc.constraint_name=kcu.constraint_name and tc.table_schema=kcu.table_schema join information_schema.constraint_column_usage ccu on ccu.constraint_name=tc.constraint_name and ccu.table_schema=tc.table_schema where tc.constraint_type='FOREIGN KEY' and tc.table_schema='store'""")
pks=sql("""select tc.table_name,kcu.column_name from information_schema.table_constraints tc join information_schema.key_column_usage kcu on tc.constraint_name=kcu.constraint_name and tc.table_schema=kcu.table_schema where tc.constraint_type='PRIMARY KEY' and tc.table_schema='store'""")
uqs=sql("""select tc.table_name,kcu.column_name from information_schema.table_constraints tc join information_schema.key_column_usage kcu on tc.constraint_name=kcu.constraint_name and tc.table_schema=kcu.table_schema where tc.constraint_type='UNIQUE' and tc.table_schema='store'""")
fk={(a,b):c for a,b,c in (x.split('\t') for x in fks)}
pk={tuple(x.split('\t')) for x in pks}
uq={tuple(x.split('\t')) for x in uqs}
tables={}
for r in cols:
    t,c,ty,n,d,_=r.split('\t')
    tables.setdefault(t,[]).append((c,ty,n,d))
json.dump({'tables':{t:[list(x)+[ 'PK' if (t,x[0]) in pk else '', 'FK→'+fk[(t,x[0])] if (t,x[0]) in fk else '', 'UQ' if (t,x[0]) in uq else '', notes.get((t,x[0]),'')] for x in v] for t,v in tables.items()}},open('/tmp/dict.json','w'),ensure_ascii=False)
print(len(tables))
