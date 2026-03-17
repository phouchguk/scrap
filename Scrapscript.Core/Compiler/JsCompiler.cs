using System.Text;
using Scrapscript.Core.Parser;

namespace Scrapscript.Core.Compiler;

public class JsCompiler
{
    private int _tmpCounter;

    private string FreshTmp() => $"_t{_tmpCounter++}";

    private static readonly HashSet<string> JsKeywords = new()
    {
        "break","case","catch","class","const","continue","debugger","default",
        "delete","do","else","export","extends","false","finally","for","function",
        "if","import","in","instanceof","let","new","null","return","static",
        "super","switch","this","throw","true","try","typeof","undefined","var",
        "void","while","with","yield"
    };

    public static string MangleIdent(string name)
    {
        var s = name.Replace("/", "$").Replace("-", "_");
        if (s.Length > 0 && char.IsDigit(s[0])) s = "$" + s;
        if (JsKeywords.Contains(s)) s = "$" + s;
        return s;
    }

    private static string QuoteString(string s)
    {
        var escaped = s
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\t", "\\t");
        return $"\"{escaped}\"";
    }

    public string Compile(Expr ast)
    {
        _tmpCounter = 0;
        return CompileExpr(ast);
    }

    private string CompileExpr(Expr e) => e switch
    {
        IntLit i    => $"{i.Value}n",
        FloatLit f  => f.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        TextLit t   => QuoteString(t.Value),
        BytesLit b  => $"new Uint8Array([{string.Join(",", b.Value)}])",
        HoleLit     => "_hole",

        Var v           => MangleIdent(v.Name),
        HashRef         => "_hole",
        MapRef          => "_hole",

        ConstructorExpr c   => $"_variant({QuoteString(c.Variant)})",
        ListExpr l          => $"[{string.Join(",", l.Items.Select(CompileExpr))}]",
        RecordExpr r        => CompileRecord(r),
        RecordAccess ra     => $"({CompileExpr(ra.Record)})[{QuoteString(ra.Field)}]",

        WhereExpr w         => CompileWhere(w),
        TypeAnnotation ta   => CompileExpr(ta.Value),
        TypeDefExpr         => "_hole",

        LambdaExpr la   => CompileLambda(la),
        CaseExpr ce     => CompileCase(ce),

        ApplyExpr a     => $"_apply({CompileExpr(a.Fn)},{CompileExpr(a.Arg)})",
        BinOpExpr b     => CompileBinOp(b),
        NegExpr n       => $"(-({CompileExpr(n.Operand)}))",

        _ => throw new NotSupportedException($"Cannot compile: {e}")
    };

    private string CompileRecord(RecordExpr r)
    {
        var parts = new List<string>();
        if (r.Spread != null)
            parts.Add($"...{MangleIdent(r.Spread)}");
        foreach (var (field, val) in r.Fields)
            parts.Add($"{QuoteString(field)}:{CompileExpr(val)}");
        return $"({{{string.Join(",", parts)}}})";
    }

    private string CompileWhere(WhereExpr w)
    {
        var sb = new StringBuilder();
        sb.Append("(()=>{");

        var sorted = SortBindings(w.Bindings);

        // Hoist all simple (VarPat) names so lambdas can reference each other
        foreach (var b in sorted)
            if (b.Pattern is VarPat vp)
                sb.Append($"var {MangleIdent(vp.Name)};");

        foreach (var b in sorted)
        {
            if (b.Pattern is VarPat vp)
            {
                sb.Append($"{MangleIdent(vp.Name)}={CompileExpr(b.Value)};");
            }
            else
            {
                var tmp = FreshTmp();
                sb.Append($"var {tmp}={CompileExpr(b.Value)};");
                var (conds, decls) = CompilePattern(b.Pattern, tmp);
                if (conds.Count > 0)
                    sb.Append($"if(!({string.Join("&&", conds)}))throw new Error(\"where: pattern match failed\");");
                foreach (var decl in decls)
                    sb.Append(decl);
            }
        }

        sb.Append($"return {CompileExpr(w.Body)};");
        sb.Append("})()");
        return sb.ToString();
    }

    private string CompileLambda(LambdaExpr la)
    {
        if (la.Param is VarPat vp)
            return $"(({MangleIdent(vp.Name)})=>{CompileExpr(la.Body)})";

        if (la.Param is WildcardPat)
            return $"((_)=>{CompileExpr(la.Body)})";

        var arg = FreshTmp();
        var sb = new StringBuilder();
        sb.Append($"(({arg})=>{{");
        var (conds, decls) = CompilePattern(la.Param, arg);
        if (conds.Count > 0)
            sb.Append($"if(!({string.Join("&&", conds)}))throw new Error(\"lambda: pattern match failed\");");
        foreach (var decl in decls)
            sb.Append(decl);
        sb.Append($"return {CompileExpr(la.Body)};");
        sb.Append("})");
        return sb.ToString();
    }

    private string CompileCase(CaseExpr ce)
    {
        var arg = FreshTmp();
        var sb = new StringBuilder();
        sb.Append($"(({arg})=>{{");
        foreach (var arm in ce.Arms)
        {
            var (conds, decls) = CompilePattern(arm.Pattern, arg);
            var guard = conds.Count == 0 ? "true" : string.Join("&&", conds);
            sb.Append($"{{if({guard}){{");
            foreach (var decl in decls)
                sb.Append(decl);
            sb.Append($"return {CompileExpr(arm.Body)};");
            sb.Append("}}");
        }
        sb.Append("throw new Error(\"Non-exhaustive match\");");
        sb.Append("})");
        return sb.ToString();
    }

    private string CompileBinOp(BinOpExpr b)
    {
        if (b.Op == ">>")
        {
            var tmp = FreshTmp();
            return $"(({tmp})=>_apply({CompileExpr(b.Right)},_apply({CompileExpr(b.Left)},{tmp})))";
        }

        var l = CompileExpr(b.Left);
        var r = CompileExpr(b.Right);

        return b.Op switch
        {
            "+"  => $"({l}+{r})",
            "-"  => $"({l}-{r})",
            "*"  => $"({l}*{r})",
            "/"  => $"({l}/{r})",
            "%"  => $"({l}%{r})",
            "++" => $"_concat({l},{r})",
            "+<" => $"_append({l},{r})",
            ">+" => $"_cons({l},{r})",
            "==" => $"_bool(_eq({l},{r}))",
            "!=" => $"_bool(!_eq({l},{r}))",
            "<"  => $"_bool({l}<{r})",
            ">"  => $"_bool({l}>{r})",
            "<=" => $"_bool({l}<={r})",
            ">=" => $"_bool({l}>={r})",
            _    => throw new NotSupportedException($"Unknown operator: {b.Op}")
        };
    }

    private (List<string> Conditions, List<string> Declarations) CompilePattern(Pattern p, string s)
    {
        var conds = new List<string>();
        var decls = new List<string>();
        CompilePatternInto(p, s, conds, decls);
        return (conds, decls);
    }

    private void CompilePatternInto(Pattern p, string s, List<string> conds, List<string> decls)
    {
        switch (p)
        {
            case WildcardPat:
                break;

            case HolePat:
                conds.Add($"_is_hole({s})");
                break;

            case VarPat vp:
                decls.Add($"var {MangleIdent(vp.Name)}={s};");
                break;

            case IntPat ip:
                conds.Add($"{s}==={ip.Value}n");
                break;

            case TextPat tp:
                if (tp.RestName == null)
                    conds.Add($"{s}==={QuoteString(tp.Prefix)}");
                else
                {
                    conds.Add($"{s}.startsWith({QuoteString(tp.Prefix)})");
                    decls.Add($"var {MangleIdent(tp.RestName)}={s}.slice({tp.Prefix.Length});");
                }
                break;

            case BytesPat bp:
                conds.Add($"_bytes_eq({s},new Uint8Array([{string.Join(",", bp.Value)}]))");
                break;

            case ListPat lp:
                conds.Add(lp.Tail == null
                    ? $"{s}.length==={lp.Items.Count}"
                    : $"{s}.length>={lp.Items.Count}");
                for (int i = 0; i < lp.Items.Count; i++)
                    CompilePatternInto(lp.Items[i], $"{s}[{i}]", conds, decls);
                if (lp.Tail != null)
                    decls.Add($"var {MangleIdent(lp.Tail)}={s}.slice({lp.Items.Count});");
                break;

            case ConsPat cp:
                conds.Add($"{s}.length>=1");
                CompilePatternInto(cp.Head, $"{s}[0]", conds, decls);
                CompilePatternInto(cp.Tail, $"{s}.slice(1)", conds, decls);
                break;

            case RecordPat rp:
                foreach (var (field, pat) in rp.Fields)
                {
                    conds.Add($"({s})[{QuoteString(field)}]!==undefined");
                    CompilePatternInto(pat, $"({s})[{QuoteString(field)}]", conds, decls);
                }
                if (rp.Spread == null)
                    conds.Add($"Object.keys({s}).length==={rp.Fields.Count}");
                else
                    decls.Add($"var {MangleIdent(rp.Spread)}=_record_spread({s},[{string.Join(",", rp.Fields.Select(f => QuoteString(f.Field)))}]);");
                break;

            case VariantPat vp:
                conds.Add($"{s}._tag==={QuoteString(vp.Tag)}");
                if (vp.Payload != null)
                    CompilePatternInto(vp.Payload, $"{s}._val", conds, decls);
                break;
        }
    }

    // ── Binding dependency sort ───────────────────────────────────────────────

    // Topologically sort bindings so that strictly-evaluated deps come before
    // their dependents. Lambdas/case exprs have no immediate deps (closures are lazy).
    private static List<Binding> SortBindings(List<Binding> bindings)
    {
        var localNames = bindings
            .Where(b => b.Pattern is VarPat)
            .Select(b => ((VarPat)b.Pattern).Name)
            .ToHashSet();

        var deps = bindings.ToDictionary(
            b => b,
            b => ImmediateVars(b.Value).Where(n => localNames.Contains(n)).ToHashSet()
        );

        var inDeg = bindings.ToDictionary(b => b, b => deps[b].Count);

        var dependents = new Dictionary<string, List<Binding>>();
        foreach (var b in bindings)
            foreach (var dep in deps[b])
            {
                if (!dependents.ContainsKey(dep)) dependents[dep] = [];
                dependents[dep].Add(b);
            }

        var queue = new Queue<Binding>(bindings.Where(b => inDeg[b] == 0));
        var sorted = new List<Binding>();

        while (queue.Count > 0)
        {
            var b = queue.Dequeue();
            sorted.Add(b);
            if (b.Pattern is VarPat vp && dependents.TryGetValue(vp.Name, out var downstream))
                foreach (var dep in downstream)
                    if (--inDeg[dep] == 0) queue.Enqueue(dep);
        }

        // Circular deps (or complex patterns) appended at end
        sorted.AddRange(bindings.Except(sorted));
        return sorted;
    }

    // Collect Var names that are evaluated strictly (immediately), i.e. NOT inside
    // a lambda or case expression body (those are captured lazily by closure).
    private static HashSet<string> ImmediateVars(Expr e)
    {
        var result = new HashSet<string>();
        CollectImmediate(e, result);
        return result;
    }

    private static void CollectImmediate(Expr e, HashSet<string> result)
    {
        switch (e)
        {
            case Var v:       result.Add(v.Name); break;
            case LambdaExpr:  break;  // body evaluated lazily
            case CaseExpr:    break;  // body evaluated lazily
            case ApplyExpr a:
                CollectImmediate(a.Fn, result);
                CollectImmediate(a.Arg, result);
                break;
            case BinOpExpr b:
                // ">>" compiles to a lambda wrapper — operands are lazy
                if (b.Op != ">>")
                {
                    CollectImmediate(b.Left, result);
                    CollectImmediate(b.Right, result);
                }
                break;
            case NegExpr n:
                CollectImmediate(n.Operand, result);
                break;
            case ListExpr l:
                foreach (var item in l.Items) CollectImmediate(item, result);
                break;
            case RecordExpr r:
                foreach (var (_, val) in r.Fields) CollectImmediate(val, result);
                break;
            case RecordAccess ra:
                CollectImmediate(ra.Record, result);
                break;
            case TypeAnnotation ta:
                CollectImmediate(ta.Value, result);
                break;
            case WhereExpr w:
                // Nested where: body free vars minus local names
                var localNames = w.Bindings
                    .Where(b => b.Pattern is VarPat)
                    .Select(b => ((VarPat)b.Pattern).Name)
                    .ToHashSet();
                var bodyVars = ImmediateVars(w.Body);
                foreach (var v in bodyVars)
                    if (!localNames.Contains(v)) result.Add(v);
                break;
        }
    }

    // ── JS Runtime ────────────────────────────────────────────────────────────

    public static readonly string Runtime = """
"use strict";
const _hole = Object.freeze({_type:"hole"});
function _is_hole(v){return v!=null&&v._type==="hole";}
function _variant(tag){return {_tag:tag,_val:null};}
function _apply(fn,arg){
  if(typeof fn==="function")return fn(arg);
  if(fn!=null&&fn._tag!==undefined){
    if(fn._val===null)return{_tag:fn._tag,_val:arg};
    if(Array.isArray(fn._val))return{_tag:fn._tag,_val:[...fn._val,arg]};
    return{_tag:fn._tag,_val:[fn._val,arg]};
  }
  throw new Error("Not applicable: "+_display(fn));
}
function _eq(a,b){
  if(a===b)return true;
  if(typeof a!==typeof b){
    if(typeof a==="bigint"||typeof b==="bigint")return false;
  }
  if(typeof a==="bigint"&&typeof b==="bigint")return a===b;
  if(a instanceof Uint8Array&&b instanceof Uint8Array){
    if(a.length!==b.length)return false;
    return a.every((v,i)=>v===b[i]);
  }
  if(Array.isArray(a)&&Array.isArray(b)){
    if(a.length!==b.length)return false;
    return a.every((v,i)=>_eq(v,b[i]));
  }
  if(a!=null&&b!=null&&typeof a==="object"&&typeof b==="object"){
    if(a._tag!==undefined||b._tag!==undefined){
      if(a._tag!==b._tag)return false;
      return _eq(a._val,b._val);
    }
    const ak=Object.keys(a).sort(),bk=Object.keys(b).sort();
    if(ak.length!==bk.length||!ak.every((k,i)=>k===bk[i]))return false;
    return ak.every(k=>_eq(a[k],b[k]));
  }
  return false;
}
function _bool(v){return v?{_tag:"true",_val:null}:{_tag:"false",_val:null};}
function _concat(a,b){
  if(typeof a==="string")return a+b;
  if(Array.isArray(a))return[...a,...b];
  if(a instanceof Uint8Array){const r=new Uint8Array(a.length+b.length);r.set(a);r.set(b,a.length);return r;}
  throw new Error("concat: unsupported type");
}
function _append(a,b){
  if(Array.isArray(a))return[...a,b];
  if(a instanceof Uint8Array&&b instanceof Uint8Array){const r=new Uint8Array(a.length+b.length);r.set(a);r.set(b,a.length);return r;}
  throw new Error("append: unsupported type");
}
function _cons(h,t){
  if(Array.isArray(t))return[h,...t];
  throw new Error("cons: right side must be a list");
}
function _bytes_eq(a,b){
  if(!(a instanceof Uint8Array)||!(b instanceof Uint8Array))return false;
  if(a.length!==b.length)return false;
  return a.every((v,i)=>v===b[i]);
}
function _record_spread(rec,excludeKeys){
  const r={};
  for(const k of Object.keys(rec)){if(!excludeKeys.includes(k))r[k]=rec[k];}
  return r;
}
function _display(v){
  if(v===null||v===undefined||_is_hole(v))return"()";
  if(typeof v==="bigint")return String(v);
  if(typeof v==="number"){
    if(Number.isInteger(v))return v.toFixed(1);
    return String(v);
  }
  if(typeof v==="string"){
    return'"'+v.replace(/\\/g,'\\\\').replace(/"/g,'\\"').replace(/\n/g,'\\n').replace(/\t/g,'\\t')+'"';
  }
  if(v instanceof Uint8Array){
    if(v.length===1)return"~"+Array.from(v).map(b=>b.toString(16).toUpperCase().padStart(2,'0')).join('');
    return"~~"+Buffer.from(v).toString('base64');
  }
  if(Array.isArray(v))return"["+v.map(_display).join(", ")+"]";
  if(v._tag!==undefined){
    if(v._val===null)return"#"+v._tag;
    if(Array.isArray(v._val))return"#"+v._tag+" "+v._val.map(_display).join(" ");
    return"#"+v._tag+" "+_display(v._val);
  }
  if(typeof v==="function")return"<function>";
  if(typeof v==="object"){
    const fields=Object.keys(v).map(k=>k+" = "+_display(v[k])).join(", ");
    return"{ "+fields+" }";
  }
  return String(v);
}
// ── Builtins ─────────────────────────────────────────────────────────────────
const to_float=(v)=>{if(typeof v==="bigint")return Number(v);if(typeof v==="number")return v;throw new Error("to-float: expected int");};
const round=(v)=>{if(typeof v==="number")return BigInt(Math.round(v));throw new Error("round");};
const ceil=(v)=>{if(typeof v==="number")return BigInt(Math.ceil(v));throw new Error("ceil");};
const floor=(v)=>{if(typeof v==="number")return BigInt(Math.floor(v));throw new Error("floor");};
const abs=(v)=>{if(typeof v==="bigint")return v<0n?-v:v;if(typeof v==="number")return Math.abs(v);throw new Error("abs");};
const min=(a)=>(b)=>a<=b?a:b;
const max=(a)=>(b)=>a>=b?a:b;
const bytes$to_utf8_text=(v)=>Buffer.from(v).toString("utf8");
const list$first=(v)=>v.length>0?{_tag:"just",_val:v[0]}:{_tag:"nothing",_val:null};
const list$length=(v)=>BigInt(v.length);
const list$repeat=(n)=>(v)=>Array(Number(n)).fill(v);
const list$map=(f)=>(lst)=>lst.map(x=>_apply(f,x));
const list$filter=(f)=>(lst)=>lst.filter(x=>_apply(f,x)._tag==="true");
const list$fold=(f)=>(init)=>(lst)=>lst.reduce((acc,x)=>_apply(_apply(f,acc),x),init);
const list$reverse=(v)=>[...v].reverse();
const list$sort=(v)=>[...v].sort((a,b)=>{if(typeof a==="bigint")return a<b?-1:a>b?1:0;if(typeof a==="number")return a-b;if(typeof a==="string")return a<b?-1:a>b?1:0;throw new Error("list/sort: unsupported");});
const list$zip=(a)=>(b)=>a.map((x,i)=>[x,b[i]]);
const text$length=(v)=>BigInt(v.length);
const text$repeat=(n)=>(v)=>v.repeat(Number(n));
const text$trim=(v)=>v.trim();
const text$split=(sep)=>(v)=>v.split(sep);
const text$to_upper=(v)=>v.toUpperCase();
const text$to_lower=(v)=>v.toLowerCase();
const maybe$default=(def)=>(m)=>m._tag==="just"?m._val:def;
const string$join=(sep)=>(lst)=>lst.join(sep);
const dict$get=(key)=>(dict)=>key in dict?{_tag:"just",_val:dict[key]}:{_tag:"nothing",_val:null};
const $true={_tag:"true",_val:null};
const $false={_tag:"false",_val:null};
""";
}
