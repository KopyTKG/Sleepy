package main

import "core:encoding/json"
import "core:fmt"
import "core:os"
import "core:slice"
import "core:strconv"
import "core:strings"

// ---------------------------------------------------------------------------
// sleepy: OpenAPI 3 JSON  ->  Insomnia v4 export JSON
// ---------------------------------------------------------------------------

VERSION :: "0.2.0"

MAX_DEPTH :: 6

// ------------------------- json.Value helpers ------------------------------

obj_get :: proc(v: json.Value, key: string) -> (json.Value, bool) {
	o, ok := v.(json.Object)
	if !ok {
		return nil, false
	}
	val, found := o[key]
	return val, found
}

as_string :: proc(v: json.Value) -> (string, bool) {
	#partial switch s in v {
	case json.String:
		return string(s), true
	}
	return "", false
}

get_string :: proc(v: json.Value, key: string) -> (string, bool) {
	val, ok := obj_get(v, key)
	if !ok {
		return "", false
	}
	return as_string(val)
}

as_object :: proc(v: json.Value) -> (json.Object, bool) {
	o, ok := v.(json.Object)
	return o, ok
}

as_array :: proc(v: json.Value) -> (json.Array, bool) {
	a, ok := v.(json.Array)
	return a, ok
}

// ------------------------- output construction -----------------------------

vstr :: proc(s: string) -> json.Value {
	return json.String(s)
}

vbool :: proc(b: bool) -> json.Value {
	return json.Boolean(b)
}

vint :: proc(i: i64) -> json.Value {
	return json.Integer(i)
}

new_object :: proc() -> json.Object {
	return make(json.Object)
}

new_array :: proc() -> json.Array {
	return make(json.Array)
}

// ------------------------- slug --------------------------------------------

slug :: proc(s: string) -> string {
	b := strings.builder_make()
	prev_underscore := false
	for r in s {
		lower: rune = r
		if r >= 'A' && r <= 'Z' {
			lower = r + 32
		}
		is_alnum := (lower >= 'a' && lower <= 'z') || (lower >= '0' && lower <= '9')
		if is_alnum {
			strings.write_rune(&b, lower)
			prev_underscore = false
		} else {
			if !prev_underscore {
				strings.write_rune(&b, '_')
				prev_underscore = true
			}
		}
	}
	out := strings.to_string(b)
	out = strings.trim(out, "_")
	return out
}

// ------------------------- schema -> example -------------------------------

resolve_ref :: proc(root: json.Value, ref: string) -> (json.Value, bool) {
	// ref like "#/components/schemas/Foo"
	if !strings.has_prefix(ref, "#/") {
		return nil, false
	}
	path := ref[2:]
	parts := strings.split(path, "/")
	defer delete(parts)
	cur := root
	for p in parts {
		next, ok := obj_get(cur, p)
		if !ok {
			return nil, false
		}
		cur = next
	}
	return cur, true
}

seen_contains :: proc(seen: []string, ref: string) -> bool {
	for s in seen {
		if s == ref {
			return true
		}
	}
	return false
}

example_for :: proc(schema: json.Value, root: json.Value, depth: int, seen: []string) -> json.Value {
	if depth > MAX_DEPTH {
		return nil
	}
	sobj, is_obj := as_object(schema)
	if !is_obj {
		// A non-object schema node (e.g. bare bool) -> null
		return nil
	}

	// $ref
	if ref, ok := get_string(schema, "$ref"); ok {
		if seen_contains(seen, ref) {
			return nil
		}
		resolved, found := resolve_ref(root, ref)
		if !found {
			return nil
		}
		new_seen := make([]string, len(seen) + 1)
		copy(new_seen, seen)
		new_seen[len(seen)] = ref
		defer delete(new_seen)
		return example_for(resolved, root, depth + 1, new_seen)
	}

	// allOf -> merge object results
	if all_of, ok := obj_get(schema, "allOf"); ok {
		if arr, is_arr := as_array(all_of); is_arr {
			merged := new_object()
			for sub in arr {
				v := example_for(sub, root, depth + 1, seen)
				if vo, vok := as_object(v); vok {
					for k, val in vo {
						merged[k] = val
					}
				}
			}
			return merged
		}
	}

	// oneOf / anyOf -> first
	for key in ([]string{"oneOf", "anyOf"}) {
		if node, ok := obj_get(schema, key); ok {
			if arr, is_arr := as_array(node); is_arr && len(arr) > 0 {
				return example_for(arr[0], root, depth + 1, seen)
			}
		}
	}

	// enum -> first value
	if node, ok := obj_get(schema, "enum"); ok {
		if arr, is_arr := as_array(node); is_arr && len(arr) > 0 {
			return arr[0]
		}
	}

	// explicit example
	if node, ok := obj_get(schema, "example"); ok {
		return node
	}

	// by type
	stype, _ := get_string(schema, "type")
	switch stype {
	case "string":
		format, _ := get_string(schema, "format")
		switch format {
		case "date-time":
			return vstr("2020-01-01T00:00:00Z")
		case "date":
			return vstr("2020-01-01")
		case "uuid":
			return vstr("00000000-0000-0000-0000-000000000000")
		}
		return vstr("")
	case "integer", "number":
		return vint(0)
	case "boolean":
		return vbool(false)
	case "array":
		items, ok := obj_get(schema, "items")
		out := new_array()
		if ok {
			v := example_for(items, root, depth + 1, seen)
			// mirror JS .filter(v => v !== undefined); keep nulls out only if truly absent
			append(&out, v)
		}
		return out
	case:
		// object or unspecified
		props, ok := obj_get(schema, "properties")
		out := new_object()
		if ok {
			if pobj, is_pobj := as_object(props); is_pobj {
				for k, v in pobj {
					out[k] = example_for(v, root, depth + 1, seen)
				}
			}
		}
		return out
	}
}

// ------------------------- JSON pretty writer ------------------------------

write_escaped_string :: proc(b: ^strings.Builder, s: string) {
	strings.write_rune(b, '"')
	for r in s {
		switch r {
		case '"':
			strings.write_string(b, "\\\"")
		case '\\':
			strings.write_string(b, "\\\\")
		case '\n':
			strings.write_string(b, "\\n")
		case '\r':
			strings.write_string(b, "\\r")
		case '\t':
			strings.write_string(b, "\\t")
		case '\b':
			strings.write_string(b, "\\b")
		case '\f':
			strings.write_string(b, "\\f")
		case:
			if r < 0x20 {
				fmt.sbprintf(b, "\\u%04x", r)
			} else {
				strings.write_rune(b, r)
			}
		}
	}
	strings.write_rune(b, '"')
}

write_indent :: proc(b: ^strings.Builder, n: int) {
	for _ in 0 ..< n {
		strings.write_string(b, "  ")
	}
}

write_float :: proc(b: ^strings.Builder, f: f64) {
	buf: [384]u8
	str := strconv.write_float(buf[:], f, 'f', -1, 64)
	// strconv.ftoa may emit a leading '+'; trim it
	if len(str) > 0 && str[0] == '+' {
		str = str[1:]
	}
	strings.write_string(b, str)
}

write_json :: proc(b: ^strings.Builder, v: json.Value, indent: int) {
	switch val in v {
	case json.Null:
		strings.write_string(b, "null")
	case json.Integer:
		fmt.sbprintf(b, "%d", val)
	case json.Float:
		write_float(b, f64(val))
	case json.Boolean:
		strings.write_string(b, val ? "true" : "false")
	case json.String:
		write_escaped_string(b, string(val))
	case json.Array:
		if len(val) == 0 {
			strings.write_string(b, "[]")
			return
		}
		strings.write_string(b, "[\n")
		for elem, i in val {
			write_indent(b, indent + 1)
			write_json(b, elem, indent + 1)
			if i < len(val) - 1 {
				strings.write_string(b, ",")
			}
			strings.write_string(b, "\n")
		}
		write_indent(b, indent)
		strings.write_string(b, "]")
	case json.Object:
		if len(val) == 0 {
			strings.write_string(b, "{}")
			return
		}
		// stable, deterministic key order
		keys := make([dynamic]string)
		defer delete(keys)
		for k in val {
			append(&keys, k)
		}
		slice.sort(keys[:])
		strings.write_string(b, "{\n")
		for k, i in keys {
			write_indent(b, indent + 1)
			write_escaped_string(b, k)
			strings.write_string(b, ": ")
			write_json(b, val[k], indent + 1)
			if i < len(keys) - 1 {
				strings.write_string(b, ",")
			}
			strings.write_string(b, "\n")
		}
		write_indent(b, indent)
		strings.write_string(b, "}")
	case:
		strings.write_string(b, "null")
	}
}

marshal_pretty :: proc(v: json.Value) -> string {
	b := strings.builder_make()
	write_json(&b, v, 0)
	return strings.to_string(b)
}

// ------------------------- Insomnia build ----------------------------------

WORKSPACE_ID :: "wrk_sleepy"
ENVIRONMENT_ID :: "env_sleepy_base"

HTTP_METHODS :: []string{"get", "post", "put", "delete", "patch"}

build_insomnia :: proc(spec: json.Value, source_name: string, base_url: string) -> json.Value {
	resources := new_array()

	// 1. workspace
	ws := new_object()
	ws["_id"] = vstr(WORKSPACE_ID)
	ws["_type"] = vstr("workspace")
	ws["parentId"] = json.Null(nil)
	title := "OpenAPI Import"
	if info, ok := obj_get(spec, "info"); ok {
		if t, tok := get_string(info, "title"); tok && t != "" {
			title = t
		}
	}
	ws["name"] = vstr(title)
	ws["description"] = vstr("")
	ws["scope"] = vstr("collection")
	append(&resources, json.Value(ws))

	// 2. base environment (child of workspace)
	env := new_object()
	env["_id"] = vstr(ENVIRONMENT_ID)
	env["_type"] = vstr("environment")
	env["parentId"] = vstr(WORKSPACE_ID)
	env["name"] = vstr("Base Environment")
	env_data := new_object()
	env_data["baseUrl"] = vstr(base_url)
	env_data["bearer"] = vstr("")
	env["data"] = json.Value(env_data)
	append(&resources, json.Value(env))

	// Collect operations and tags.
	Op :: struct {
		method:      string,
		path:        string,
		tag:         string,
		operationId: string,
		summary:     string,
		op:          json.Value,
	}
	ops := make([dynamic]Op)
	defer delete(ops)
	tag_set := make(map[string]bool)
	defer delete(tag_set)

	paths, has_paths := obj_get(spec, "paths")
	if has_paths {
		if pobj, ok := as_object(paths); ok {
			// deterministic path iteration
			pkeys := make([dynamic]string)
			defer delete(pkeys)
			for k in pobj {
				append(&pkeys, k)
			}
			slice.sort(pkeys[:])
			for path in pkeys {
				item := pobj[path]
				for method in HTTP_METHODS {
					opval, hasm := obj_get(item, method)
					if !hasm {
						continue
					}

					tag := "default"
					if tags, tok := obj_get(opval, "tags"); tok {
						if tarr, is_arr := as_array(tags); is_arr && len(tarr) > 0 {
							if ts, sok := as_string(tarr[0]); sok && ts != "" {
								tag = ts
							}
						}
					}

					operationId, _ := get_string(opval, "operationId")
					if operationId == "" {
						operationId = fmt.aprintf("%s_%s", method, path)
					}

					summary, _ := get_string(opval, "summary")
					if summary == "" {
						summary, _ = get_string(opval, "description")
					}

					tag_set[tag] = true
					append(
						&ops,
						Op {
							method = method,
							path = path,
							tag = tag,
							operationId = operationId,
							summary = summary,
							op = opval,
						},
					)
				}
			}
		}
	}

	// 3. folders (request_group) per tag, sorted
	tags := make([dynamic]string)
	defer delete(tags)
	for t in tag_set {
		append(&tags, t)
	}
	slice.sort(tags[:])

	folder_id_for := make(map[string]string)
	defer delete(folder_id_for)
	for tag in tags {
		fid := fmt.aprintf("fld_%s", slug(tag))
		folder_id_for[tag] = fid
		f := new_object()
		f["_id"] = vstr(fid)
		f["_type"] = vstr("request_group")
		f["parentId"] = vstr(WORKSPACE_ID)
		f["name"] = vstr(tag)
		append(&resources, json.Value(f))
	}

	// 4. requests, one per operation
	for o in ops {
		req := new_object()
		req["_id"] = vstr(fmt.aprintf("req_%s", slug(o.operationId)))
		req["_type"] = vstr("request")
		req["parentId"] = vstr(folder_id_for[o.tag])
		req["name"] = vstr(fmt.aprintf("%s %s", strings.to_upper(o.method), o.path))
		req["method"] = vstr(strings.to_upper(o.method))
		// NOTE: build the URL by concatenation, not fmt.aprintf: Odin's printf
		// treats "{{"/"}}" as escaped braces, which would mangle the template.
		req["url"] = vstr(strings.concatenate({"{{ _.baseUrl }}", o.path}))
		req["description"] = vstr(o.summary)

		has_body := false
		body_schema: json.Value
		if rb, ok := obj_get(o.op, "requestBody"); ok {
			has_body = true
			if content, cok := obj_get(rb, "content"); cok {
				if appjson, jok := obj_get(content, "application/json"); jok {
					if sch, sok := obj_get(appjson, "schema"); sok {
						body_schema = sch
					}
				}
			}
		}

		// headers
		headers := new_array()
		if has_body {
			h := new_object()
			h["name"] = vstr("Content-Type")
			h["value"] = vstr("application/json")
			append(&headers, json.Value(h))
		}
		req["headers"] = json.Value(headers)

		// query parameters
		params := new_array()
		if plist, ok := obj_get(o.op, "parameters"); ok {
			if parr, is_arr := as_array(plist); is_arr {
				for p in parr {
					pin, _ := get_string(p, "in")
					if pin != "query" {
						continue
					}
					pname, _ := get_string(p, "name")
					pdesc, _ := get_string(p, "description")
					pm := new_object()
					pm["name"] = vstr(pname)
					pm["value"] = vstr("")
					pm["disabled"] = vbool(false)
					pm["description"] = vstr(pdesc)
					append(&params, json.Value(pm))
				}
			}
		}
		req["parameters"] = json.Value(params)

		// authentication (generic bearer)
		auth := new_object()
		auth["type"] = vstr("bearer")
		auth["token"] = vstr("{{ _.bearer }}")
		req["authentication"] = json.Value(auth)

		// body
		if body_schema != nil {
			example := example_for(body_schema, spec, 0, {})
			text := marshal_pretty(example)
			body := new_object()
			body["mimeType"] = vstr("application/json")
			body["text"] = vstr(text)
			req["body"] = json.Value(body)
		} else {
			req["body"] = json.Value(new_object())
		}

		append(&resources, json.Value(req))
	}

	// top-level export
	root := new_object()
	root["_type"] = vstr("export")
	root["__export_format"] = vint(4)
	root["__export_source"] = vstr(fmt.aprintf("sleepy:%s", source_name))
	root["__export_date"] = vstr("2020-01-01T00:00:00Z")
	root["resources"] = json.Value(resources)
	return root
}

// ------------------------- URL / fetch -------------------------------------

is_url :: proc(s: string) -> bool {
	return strings.has_prefix(s, "http://") || strings.has_prefix(s, "https://")
}

// Return the origin (scheme://host[:port]) of a URL, i.e. everything up to the
// first path separator. "https://api.example.com/v1/openapi.json" ->
// "https://api.example.com". If there is no path component the whole URL is
// returned (with a trailing "/" trimmed).
url_origin :: proc(url: string) -> string {
	sep := strings.index(url, "://")
	if sep < 0 {
		return strings.trim_suffix(url, "/")
	}
	rest_start := sep + 3
	rest := url[rest_start:]
	slash := strings.index_byte(rest, '/')
	if slash < 0 {
		return strings.trim_suffix(url, "/")
	}
	return url[:rest_start + slash]
}

// Fetch a URL by shelling out to `curl`. Odin's standard library has no HTTPS
// client, and most OpenAPI specs live behind https, so we lean on curl.
fetch_url :: proc(url: string) -> ([]byte, bool) {
	state, out, errout, err := os.process_exec(
		os.Process_Desc{command = {"curl", "-sSfL", url}},
		context.allocator,
	)
	if err != nil {
		fmt.eprintfln("error: failed to run curl (is it installed and on PATH?): %v", err)
		return nil, false
	}
	defer delete(errout)
	if !state.success || state.exit_code != 0 {
		fmt.eprintfln("error: curl failed (exit %d) fetching %s", state.exit_code, url)
		if len(errout) > 0 {
			fmt.eprint(string(errout))
		}
		delete(out)
		return nil, false
	}
	return out, true
}

// ------------------------- IO / CLI ----------------------------------------

read_all_stdin :: proc() -> ([]byte, bool) {
	data := make([dynamic]u8)
	buf: [4096]u8
	for {
		n, err := os.read(os.stdin, buf[:])
		if n > 0 {
			append(&data, ..buf[:n])
		}
		if n <= 0 || err != nil {
			break
		}
	}
	return data[:], true
}

usage :: proc() {
	fmt.eprintln("sleepy - convert an OpenAPI 3 JSON spec into an Insomnia v4 export")
	fmt.eprintln("")
	fmt.eprintln("usage: sleepy <spec.json|url> [--base-url <url>] [-o <out.json>]")
	fmt.eprintln("       sleepy - [--base-url <url>] [-o <out.json>]   (read spec from stdin)")
	fmt.eprintln("")
	fmt.eprintln("If the input is an http(s) URL, the spec is fetched with curl and the")
	fmt.eprintln("environment's baseUrl is prefilled with the URL's origin. Use --base-url")
	fmt.eprintln("to override the baseUrl (also useful for file/stdin input).")
	fmt.eprintln("")
	fmt.eprintln("If -o is omitted the export is written to stdout.")
}

main :: proc() {
	args := os.args[1:]
	if len(args) < 1 {
		usage()
		os.exit(2)
	}

	input_path := ""
	output_path := ""
	base_url := ""
	base_url_set := false

	i := 0
	for i < len(args) {
		a := args[i]
		switch a {
		case "-o", "--output":
			if i + 1 >= len(args) {
				fmt.eprintln("error: -o requires a file argument")
				usage()
				os.exit(2)
			}
			output_path = args[i + 1]
			i += 2
		case "--base-url":
			if i + 1 >= len(args) {
				fmt.eprintln("error: --base-url requires a url argument")
				usage()
				os.exit(2)
			}
			base_url = args[i + 1]
			base_url_set = true
			i += 2
		case "-h", "--help":
			usage()
			os.exit(0)
		case "-v", "--version":
			fmt.println(VERSION)
			os.exit(0)
		case:
			if input_path == "" {
				input_path = a
			} else {
				fmt.eprintfln("error: unexpected argument: %s", a)
				usage()
				os.exit(2)
			}
			i += 1
		}
	}

	if input_path == "" {
		usage()
		os.exit(2)
	}

	// read input
	data: []byte
	source_name := input_path
	if input_path == "-" {
		ok: bool
		data, ok = read_all_stdin()
		if !ok {
			fmt.eprintln("error: failed to read from stdin")
			os.exit(1)
		}
		source_name = "stdin"
	} else if is_url(input_path) {
		ok: bool
		data, ok = fetch_url(input_path)
		if !ok {
			os.exit(1)
		}
		// Prefill baseUrl from the URL's origin unless the user overrode it.
		if !base_url_set {
			base_url = url_origin(input_path)
		}
	} else {
		rerr: os.Error
		data, rerr = os.read_entire_file(input_path, context.allocator)
		if rerr != nil {
			fmt.eprintfln("error: could not read file: %s (%v)", input_path, rerr)
			os.exit(1)
		}
	}

	spec, perr := json.parse(data, spec = .JSON5, parse_integers = true)
	if perr != nil {
		fmt.eprintfln("error: failed to parse JSON input: %v", perr)
		os.exit(1)
	}

	doc := build_insomnia(spec, source_name, base_url)
	out := marshal_pretty(doc)

	if output_path == "" {
		fmt.println(out)
	} else {
		if werr := os.write_entire_file(output_path, transmute([]byte)out); werr != nil {
			fmt.eprintfln("error: could not write file: %s (%v)", output_path, werr)
			os.exit(1)
		}
		fmt.eprintfln("wrote %s", output_path)
	}
}
