"""Pinned SQL Server schema contract for the additive delivery migration.

The builder derives the contract from the exact EF SQL shipped in the payload.
The installer compares it with SQL Server metadata, including key/index columns
and restricted foreign keys; definitions are checked only with SQL administrator.
No executable SQL or user data is accepted from configuration or the database.
"""
import json
import re

TABLES = ('CustomerDeliveryLocations', 'DeliveryAssignments', 'DeliveryEvents', 'DeliveryRoutes', 'DeliveryTrips')


def normalized(value):
    """Compare a bounded CHECK grammar without discarding its grouping.

    SQL Server adds redundant parentheses and expands IN to OR. Parsing makes
    those equivalent while retaining the distinction between A-(B+C) and
    (A-B)+C and between A AND (B OR C) and (A AND B) OR C.
    """
    if value.startswith('$expr$'):
        return value  # Already-normalized contract, not valid CHECK SQL syntax.
    pattern = re.compile(r"\s+|\[[^]]+\]|N?'(?:''|[^'])*'|>=|<=|<>|!=|[=><()+\-*/,]|[A-Za-z_][A-Za-z0-9_]*|\d+(?:\.\d+)?", re.I)
    tokens = []
    position = 0
    for match in pattern.finditer(value):
        if match.start() != position:
            raise ValueError('delivery_check_expression_unsupported')
        token = match.group()
        position = match.end()
        if token.isspace():
            continue
        if token.startswith('['):
            token = token[1:-1].casefold()
        elif token[:2].casefold() == "n'":
            token = token[1:]
        elif not token.startswith("'"):
            token = token.casefold()
        tokens.append(token)
    if position != len(value) or not tokens:
        raise ValueError('delivery_check_expression_unsupported')
    index = 0

    def take(expected=None):
        nonlocal index
        if index >= len(tokens) or (expected is not None and tokens[index] != expected):
            raise ValueError('delivery_check_expression_unsupported')
        token = tokens[index]
        index += 1
        return token

    def peek():
        return tokens[index] if index < len(tokens) else None

    def combine(operator, left, right):
        if operator in ('and', 'or'):
            children = []
            for child in (left, right):
                children.extend(child[1:] if child[0] == operator else [child])
            return [operator, *sorted(children, key=lambda child: json.dumps(child, separators=(',', ':')))]
        return [operator, left, right]

    def expression(minimum=0):
        token = take()
        if token == '(':
            left = expression()
            take(')')
        elif token in ('+', '-'):
            left = ['unary' + token, expression(6)]
        elif re.fullmatch(r'\d+(?:\.\d+)?', token):
            left = ['number', token]
        elif token.startswith("'"):
            left = ['literal', token]
        elif re.fullmatch(r'[a-z_][a-z0-9_]*', token):
            if peek() == '(':
                if token not in ('coalesce', 'isnull'):
                    raise ValueError('delivery_check_function_unsupported')
                take('(')
                arguments = [expression()]
                while peek() == ',':
                    take(',')
                    arguments.append(expression())
                take(')')
                left = ['call', token, *arguments]
            else:
                left = ['identifier', token]
        else:
            raise ValueError('delivery_check_expression_unsupported')
        precedence = {'or': 1, 'and': 2, '=': 3, '<>': 3, '!=': 3, '>': 3, '<': 3,
                      '>=': 3, '<=': 3, 'is': 3, 'in': 3, '+': 4, '-': 4, '*': 5, '/': 5}
        while peek() in precedence and precedence[peek()] >= minimum:
            operator = take()
            if operator == 'is':
                negative = peek() == 'not'
                if negative:
                    take('not')
                take('null')
                left = ['is-not-null' if negative else 'is-null', left]
            elif operator == 'in':
                take('(')
                items = [expression(4)]
                while peek() == ',':
                    take(',')
                    items.append(expression(4))
                take(')')
                alternatives = [['=', left, item] for item in items]
                left = alternatives[0]
                for alternative in alternatives[1:]:
                    left = combine('or', left, alternative)
            else:
                left = combine(operator, left, expression(precedence[operator] + 1))
        return left

    result = expression()
    if index != len(tokens):
        raise ValueError('delivery_check_expression_unsupported')
    return '$expr$' + json.dumps(result, separators=(',', ':'))


def contract_from_sql(sql):
    contract = dict(columns=[], primaryKeys=[], foreignKeys=[], indexes=[], checks=[])
    blocks = re.findall(r'CREATE TABLE \[([^]]+)\] \((.*?)^\s*\);', sql, re.M | re.S)
    if {name for name, _ in blocks} != set(TABLES) or len(blocks) != len(TABLES):
        raise ValueError('delivery_schema_tables_unexpected')
    for table, body in blocks:
        for line in body.splitlines():
            match = re.match(r'\s*\[([^]]+)\] ([a-z]+)(?:\((max|\d+)(?:,(\d+))?\))? (NOT NULL|NULL)(?:[ ,]|$)', line, re.I)
            if match:
                name, kind, size, scale, nullable = match.groups()
                if kind == 'nvarchar':
                    length, precision, decimal_scale = (-1 if size == 'max' else int(size) * 2), 0, 0
                elif kind == 'decimal':
                    precision, decimal_scale = int(size), int(scale)
                    length = 5 if precision <= 9 else 9 if precision <= 19 else 13 if precision <= 28 else 17
                else:
                    standard = {'uniqueidentifier': (16, 0, 0), 'int': (4, 10, 0), 'bigint': (8, 19, 0),
                                'bit': (1, 1, 0), 'date': (3, 10, 0), 'time': (5, 16, 7), 'datetimeoffset': (10, 34, 7)}
                    if kind not in standard:
                        raise ValueError('delivery_schema_type_unexpected')
                    length, precision, decimal_scale = standard[kind]
                contract['columns'].append(dict(table=table, name=name, type=kind, length=length,
                    precision=precision, scale=decimal_scale, nullable=int(nullable == 'NULL'),
                    defaultBinding=int(' DEFAULT ' in line.upper())))
        for name, names in re.findall(r'CONSTRAINT \[([^]]+)\] PRIMARY KEY \(([^)]+)\)', body):
            for ordinal, column in enumerate(re.findall(r'\[([^]]+)\]', names), 1):
                contract['primaryKeys'].append(dict(table=table, name=name, column=column, ordinal=ordinal, disabled=0))
        for name, expression in re.findall(r'CONSTRAINT \[([^]]+)\] CHECK \((.*)\)(?:,|\s*$)', body, re.M):
            contract['checks'].append(dict(table=table, name=name, definition=normalized(expression), disabled=0, untrusted=0))
        for name, names, target, targets, action in re.findall(
                r'CONSTRAINT \[([^]]+)\] FOREIGN KEY \(([^)]+)\) REFERENCES \[([^]]+)\] \(([^)]+)\)(?: ON DELETE (NO ACTION|CASCADE))?', body):
            if action not in ('', 'NO ACTION'):
                raise ValueError('delivery_schema_delete_action_unexpected')
            source_columns, target_columns = re.findall(r'\[([^]]+)\]', names), re.findall(r'\[([^]]+)\]', targets)
            if len(source_columns) != len(target_columns):
                raise ValueError('delivery_schema_foreign_key_invalid')
            for ordinal, (column, target_column) in enumerate(zip(source_columns, target_columns), 1):
                contract['foreignKeys'].append(dict(table=table, name=name, column=column, target=target,
                    targetColumn=target_column, ordinal=ordinal, disabled=0, untrusted=0, deleteAction=0, updateAction=0))
    for unique, name, table, names, filter_clause in re.findall(
            r'CREATE (UNIQUE )?INDEX \[([^]]+)\] ON \[([^]]+)\] \(([^)]+)\)([^;]*);', sql):
        if table not in TABLES:
            raise ValueError('delivery_schema_index_table_unexpected')
        if filter_clause.strip():
            raise ValueError('delivery_schema_filtered_index_unsupported')
        for ordinal, column in enumerate(re.findall(r'\[([^]]+)\]', names), 1):
            contract['indexes'].append(dict(table=table, name=name, column=column, ordinal=ordinal,
                unique=int(bool(unique)), disabled=0, filtered=0, descending=0))
    if not contract['columns'] or len({row['table'] for row in contract['primaryKeys']}) != len(TABLES):
        raise ValueError('delivery_schema_keys_or_columns_missing')
    return canonical(contract)


def canonical(contract):
    expected = {'columns', 'primaryKeys', 'foreignKeys', 'indexes', 'checks'}
    if not isinstance(contract, dict) or set(contract) != expected or not all(isinstance(x, list) for x in contract.values()):
        raise ValueError('delivery_schema_metadata_unexpected')
    result = {}
    for key, rows in contract.items():
        values = []
        for row in rows:
            if not isinstance(row, dict):
                raise ValueError('delivery_schema_metadata_unexpected')
            current = dict(row)
            if key == 'checks' and 'definition' in current:
                current['definition'] = normalized(current['definition'])
            values.append(current)
        result[key] = sorted(values, key=lambda x: (x.get('table', ''), x.get('name', ''), x.get('ordinal', 0)))
    return result


def schema_query(strict=True):
    names = ','.join("N'" + table + "'" for table in TABLES)
    scope = 't.schema_id=SCHEMA_ID(N\'dbo\') AND t.name IN (' + names + ')'
    definition = ',c.definition AS definition' if strict else ''
    return '''SELECT
 JSON_QUERY(COALESCE((SELECT t.name AS [table],c.name,TYPE_NAME(c.system_type_id) AS type,c.max_length AS length,
  c.precision,c.scale,CONVERT(int,c.is_nullable) AS nullable,
  CASE WHEN c.default_object_id=0 THEN 0 ELSE 1 END AS defaultBinding
  FROM sys.tables t JOIN sys.columns c ON c.object_id=t.object_id
  WHERE ''' + scope + ''' ORDER BY t.name,c.name FOR JSON PATH), N'[]')) AS [columns],
 JSON_QUERY(COALESCE((SELECT t.name AS [table],k.name,c.name AS [column],ic.key_ordinal AS ordinal,CONVERT(int,ki.is_disabled) AS disabled
  FROM sys.tables t JOIN sys.key_constraints k ON k.parent_object_id=t.object_id AND k.type=N'PK'
  JOIN sys.indexes ki ON ki.object_id=t.object_id AND ki.index_id=k.unique_index_id
  JOIN sys.index_columns ic ON ic.object_id=t.object_id AND ic.index_id=k.unique_index_id
  JOIN sys.columns c ON c.object_id=t.object_id AND c.column_id=ic.column_id
  WHERE ''' + scope + ''' ORDER BY t.name,k.name,ic.key_ordinal FOR JSON PATH), N'[]')) AS primaryKeys,
 JSON_QUERY(COALESCE((SELECT t.name AS [table],f.name,c.name AS [column],rt.name AS target,rc.name AS targetColumn,
  fk.constraint_column_id AS ordinal,CONVERT(int,f.is_disabled) AS disabled,CONVERT(int,f.is_not_trusted) AS untrusted,
  CONVERT(int,f.delete_referential_action) AS deleteAction,CONVERT(int,f.update_referential_action) AS updateAction
  FROM sys.tables t JOIN sys.foreign_keys f ON f.parent_object_id=t.object_id
  JOIN sys.foreign_key_columns fk ON fk.constraint_object_id=f.object_id
  JOIN sys.columns c ON c.object_id=t.object_id AND c.column_id=fk.parent_column_id
  JOIN sys.tables rt ON rt.object_id=f.referenced_object_id
  JOIN sys.columns rc ON rc.object_id=rt.object_id AND rc.column_id=fk.referenced_column_id
  WHERE ''' + scope + ''' ORDER BY t.name,f.name,fk.constraint_column_id FOR JSON PATH), N'[]')) AS foreignKeys,
 JSON_QUERY(COALESCE((SELECT t.name AS [table],i.name,c.name AS [column],ic.key_ordinal AS ordinal,CONVERT(int,i.is_unique) AS [unique],
  CONVERT(int,i.is_disabled) AS disabled,CONVERT(int,i.has_filter) AS filtered,CONVERT(int,ic.is_descending_key) AS descending
  FROM sys.tables t JOIN sys.indexes i ON i.object_id=t.object_id AND i.is_primary_key=0 AND i.type=2
  JOIN sys.index_columns ic ON ic.object_id=t.object_id AND ic.index_id=i.index_id AND ic.key_ordinal>0
  JOIN sys.columns c ON c.object_id=t.object_id AND c.column_id=ic.column_id
  WHERE ''' + scope + ''' ORDER BY t.name,i.name,ic.key_ordinal FOR JSON PATH), N'[]')) AS indexes,
 JSON_QUERY(COALESCE((SELECT t.name AS [table],c.name,CONVERT(int,c.is_disabled) AS disabled,CONVERT(int,c.is_not_trusted) AS untrusted''' + definition + '''
  FROM sys.tables t JOIN sys.check_constraints c ON c.parent_object_id=t.object_id
  WHERE ''' + scope + ''' ORDER BY t.name,c.name FOR JSON PATH), N'[]')) AS checks
 FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;'''


def matches(data, contract, strict=True):
    try:
        expected = canonical(contract)
        if not strict:
            expected['checks'] = [{k: v for k, v in row.items() if k != 'definition'} for row in expected['checks']]
        return canonical(data) == expected
    except (ValueError, TypeError, KeyError):
        return False
