"""C# syntax evidence, deliberately not a type-bound or dynamic call graph.

Tree-sitter identifies source structure. A call points to a syntactic reference, not
an invented callee. Event-looking += operations remain ambiguous until type-bound
or manually verified. Inactive preprocessor branches and deferred lambdas are not
claimed to execute. Private source is never transmitted to an external service.
"""
from collections import Counter, defaultdict
import hashlib
from pathlib import Path
from tree_sitter import Language, Parser
import tree_sitter_c_sharp

LANGUAGE = Language(tree_sitter_c_sharp.language())
DECLARATIONS = {'class_declaration': 'class', 'struct_declaration': 'class',
                'interface_declaration': 'interface', 'enum_declaration': 'enum',
                'record_declaration': 'class', 'method_declaration': 'method',
                'constructor_declaration': 'method', 'local_function_statement': 'method',
                'property_declaration': 'property', 'event_declaration': 'event'}
CALL_OWNERS = {'method', 'property', 'event'}


def community(path):
    if path.startswith('runtime-snapshot/Routines/Singular wotlk/'):
        parts = path.split('/')
        return 'Singular/' + (parts[4] if len(parts) > 4 and parts[3] == 'ClassSpecific' else 'foundation')
    for prefix, name in (
        ('runtime-snapshot/Bots/WholesomeAutoQuest-master/', 'Wholesome'),
        ('runtime-snapshot/Plugins/', 'plugins'), ('runtime-snapshot/Quest Behaviors/', 'quest-behaviors'),
        ('runtime-snapshot/Default Profiles/', 'profiles'), ('runtime-snapshot/Dungeon Scripts/', 'dungeons'),
        ('runtime-snapshot/Routines/', 'other-routines'), ('Styx/Logic/Questing/', 'quest-recovery'),
        ('Styx/Logic/Pathing/', 'navigation'), ('Styx/Logic/BehaviorTree/', 'scheduler-lifecycle'),
        ('Styx/Logic/Combat/', 'combat'), ('Styx/WoWInternals/', 'memory-lua-objects'),
        ('Styx/Logic/Profiles/', 'profiles'), ('Tools/', 'tests-tools'), ('Bots/', 'bot-bases'),
        ('TreeSharp/', 'behavior-tree'), ('Tripper/', 'native-navigation'), ('UI/', 'host-ui'),
        ('Styx/', 'core-managers')):
        if path.startswith(prefix):
            return name
    return path.split('/')[0] if '/' in path else 'host-build'


def extract_source(path, data):
    tree = Parser(LANGUAGE).parse(data)
    nodes, edges, errors = {}, [], []
    digest = hashlib.sha256(data).hexdigest()
    file_id = 'file:' + path
    group = community(path)
    nodes[file_id] = dict(id=file_id, type='component', label=path, community=group,
                          sha256=digest, parse_has_error=tree.root_node.has_error)
    def text(node):
        return data[node.start_byte:node.end_byte].decode('utf-8-sig', errors='replace') if node else ''
    def provenance(node):
        return [dict(path=path, start_line=node.start_point.row + 1,
                     end_line=node.end_point.row + 1, sha256=digest)]
    def add_edge(source, target, kind, node, classification='EXTRACTED', confidence=1.0, **extra):
        edges.append(dict(source=source, target=target, type=kind, classification=classification,
                          confidence=confidence, provenance=provenance(node), **extra))
    stack = [(tree.root_node, file_id, file_id, False)]
    while stack:
        node, owner, class_owner, deferred = stack.pop()
        if node.type == 'ERROR' or node.is_missing:
            errors.append(dict(line=node.start_point.row + 1, kind=node.type,
                               missing=node.is_missing))
        if node.type in DECLARATIONS:
            kind = DECLARATIONS[node.type]
            name = text(node.child_by_field_name('name')) or node.type
            ident = f'{kind}:{path}:{node.start_point.row + 1}:{node.start_point.column + 1}:{name}'
            attrs = ' '.join(text(c) for c in node.named_children if c.type == 'attribute_list')
            params = node.child_by_field_name('parameters')
            nodes[ident] = dict(id=ident, type=kind, label=name, community=group,
                                source=provenance(node)[0], attributes=attrs,
                                arity=len(params.named_children) if params else 0,
                                runtime_validation='not_executed', parse_has_error=node.has_error,
                                enclosing_class=class_owner)
            add_edge(owner, ident, 'owns', node)
            owner = ident
            if kind in {'class', 'interface', 'enum'}:
                class_owner = ident
        if node.type in {'lambda_expression', 'anonymous_method_expression'}:
            deferred = True
        if node.type == 'invocation_expression':
            function = node.child_by_field_name('function')
            function_text = text(function)
            ref_id = 'method-reference:' + function_text
            if ref_id not in nodes:
                nodes[ref_id] = dict(id=ref_id, type='method_reference', label=function_text,
                                    community='unresolved-references')
            args = node.child_by_field_name('arguments')
            add_edge(owner, ref_id, 'calls', node, resolution='syntax_only',
                     execution='deferred' if deferred else 'unspecified',
                     argument_count=len(args.named_children) if args else 0)
        if node.type == 'assignment_expression':
            left, right = node.child_by_field_name('left'), node.child_by_field_name('right')
            operator = ''.join(text(c) for c in node.children if not c.is_named).strip()
            label = text(left)
            ref_id = f'state-reference:{class_owner}:{label}'
            nodes.setdefault(ref_id, dict(id=ref_id, type='state', label=label, community=group))
            add_edge(owner, ref_id, 'writes', node, resolution='assignment_syntax', operator=operator)
            if operator in {'+=', '-='} and right and right.type in {
                'identifier', 'member_access_expression', 'lambda_expression', 'anonymous_method_expression'}:
                add_edge(owner, ref_id, 'subscribes_to' if operator == '+=' else 'unsubscribes_from', node,
                         classification='AMBIGUOUS', confidence=.5,
                         reason='Assignment syntax can denote an event, delegate or overloaded operator; no type binding.',
                         falsification='Resolve the left-hand symbol using the host compilation and verify its event type.',
                         handler=text(right)[:120] if right.type != 'lambda_expression' else '<lambda>')
        for child in reversed(node.named_children):
            stack.append((child, owner, class_owner, deferred))
    graph = dict(directed=True, nodes=list(nodes.values()), edges=edges, parse_errors=errors)
    validate_graph(graph)
    return graph


def validate_graph(graph):
    ids = {n['id'] for n in graph['nodes']}
    if len(ids) != len(graph['nodes']):
        raise ValueError('Duplicate node IDs')
    for edge in graph['edges']:
        if edge.get('source') not in ids or edge.get('target') not in ids:
            raise ValueError('Dangling graph edge')
        label = edge.get('classification')
        if label not in {'EXTRACTED', 'INFERRED', 'AMBIGUOUS'}:
            raise ValueError('Missing evidence classification')
        if not 0 <= edge.get('confidence', -1) <= 1:
            raise ValueError('Invalid confidence')
        if not edge.get('provenance'):
            raise ValueError('Missing provenance')
        for source in edge['provenance']:
            if not source.get('path') or source.get('start_line', 0) < 1:
                raise ValueError('Missing source line')
            if source.get('end_line', 0) < source['start_line']:
                raise ValueError('Reversed source span')
        if label != 'EXTRACTED' and (not edge.get('reason') or not edge.get('falsification')):
            raise ValueError('Inference needs reasoning and a falsification check')


def extract_repository(root):
    nodes, edges, parse_errors, file_counts = {}, [], [], Counter()
    for path in sorted(root.rglob('*.cs')):
        relative = path.relative_to(root).as_posix()
        if any(part in {'.git', 'obj', 'bin'} for part in path.parts):
            continue
        graph = extract_source(relative, path.read_bytes())
        nodes.update((node['id'], node) for node in graph['nodes'])
        edges.extend(graph['edges'])
        file_counts[community(relative)] += 1
        if graph['parse_errors']:
            parse_errors.append(dict(path=relative, errors=graph['parse_errors']))
    graph = dict(schema_version=1, directed=True, scope='C# syntax plus separately curated evidence; not type-bound',
                 nodes=list(nodes.values()), edges=edges, parse_errors=parse_errors,
                 source_files_by_community=dict(sorted(file_counts.items())))
    validate_graph(graph)
    return graph


def write_graphml(graph, path):
    """Portable GraphML with JSON attributes, preserving direction and evidence."""
    import json
    import xml.etree.ElementTree as ET
    namespace = 'http://graphml.graphdrawing.org/xmlns'
    root = ET.Element('graphml', xmlns=namespace)
    ET.SubElement(root, 'key', id='attributes', **{'for': 'all', 'attr.name': 'attributes', 'attr.type': 'string'})
    body = ET.SubElement(root, 'graph', id='audit', edgedefault='directed')
    for node in graph['nodes']:
        element = ET.SubElement(body, 'node', id=node['id'])
        ET.SubElement(element, 'data', key='attributes').text = json.dumps(node, ensure_ascii=True)
    for index, edge in enumerate(graph['edges']):
        element = ET.SubElement(body, 'edge', id=f'e{index}', source=edge['source'], target=edge['target'])
        ET.SubElement(element, 'data', key='attributes').text = json.dumps(edge, ensure_ascii=True)
    ET.ElementTree(root).write(path, encoding='utf-8', xml_declaration=True)
