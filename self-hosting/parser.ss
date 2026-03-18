{ parse = parse }
; parse = tokens -> (parse-expr tokens).result
; parse-expr = tokens -> parse-where tokens
; parse-where = tokens ->
    (| { result = body, rest = rest1 } ->
         (| [#semi] ++ rest2 ->
              (| { result = w, rest = rest3 } ->
                  { result = w, rest = rest3 }
              ) (parse-bindings body rest2 [])
          | _ -> { result = body, rest = rest1 }
         ) rest1
    ) (parse-lam tokens)
; parse-bindings = body -> tokens -> binds-rev ->
    (| [] ->
         { result = #where { body = body, binds = list/reverse binds-rev }, rest = [] }
     | [#ident name, #eq] ++ rest ->
         (| { result = val, rest = rest2 } ->
              (| [#semi] ++ rest3 ->
                   parse-bindings body rest3 ({ name = name, val = val } >+ binds-rev)
               | _ ->
                   { result = #where { body = body, binds = list/reverse ({ name = name, val = val } >+ binds-rev) }, rest = rest2 }
              ) rest2
         ) (parse-lam rest)
     | _ ->
         { result = #where { body = body, binds = list/reverse binds-rev }, rest = tokens }
    ) tokens
; parse-lam = tokens ->
    (| { result = p, rest = rest1 } ->
         (| [#arrow] ++ rest2 ->
              (| { result = body, rest = rest3 } ->
                  { result = #lam { param = p, body = body }, rest = rest3 }
              ) (parse-lam rest2)
          | _ -> try-binop p rest1
         ) rest1
    ) (parse-app tokens)
; parse-app = tokens ->
    (| { result = first, rest = rest1 } ->
        collect-args first rest1
    ) (parse-atom tokens)
; collect-args = fn -> tokens ->
    (| [#dot, #ident name] ++ rest ->
         collect-args (#field-access { record = fn, field = name }) rest
     | _ ->
         (| #nothing -> { result = fn, rest = tokens }
          | #just { result = arg, rest = rest2 } ->
              collect-args (#app { fn = fn, arg = arg }) rest2
         ) (parse-atom-opt tokens)
    ) tokens
; can-start-atom = tokens ->
    (| [#int _] ++ _t -> #true
     | [#float _] ++ _t -> #true
     | [#text _] ++ _t -> #true
     | [#ident _] ++ _t -> #true
     | [#tag _] ++ _t -> #true
     | [#lparen] ++ _t -> #true
     | [#lbracket] ++ _t -> #true
     | [#lbrace] ++ _t -> #true
     | [#pipe] ++ _t -> #true
     | [#minus, #int _] ++ _t -> #true
     | [#minus, #float _] ++ _t -> #true
     | _ -> #false
    ) tokens
; parse-atom-opt = tokens ->
    (| #true -> #just (parse-atom tokens)
     | #false -> #nothing
    ) (can-start-atom tokens)
; parse-atom = tokens ->
    (| [#int n] ++ rest -> { result = #lit-int n, rest = rest }
     | [#float f] ++ rest -> { result = #lit-float f, rest = rest }
     | [#text t] ++ rest -> { result = #lit-text t, rest = rest }
     | [#minus, #int n] ++ rest -> { result = #lit-int (0 - n), rest = rest }
     | [#minus, #float f] ++ rest -> { result = #lit-float (0.0 - f), rest = rest }
     | [#ident name] ++ rest -> { result = #var name, rest = rest }
     | [#tag name] ++ rest -> { result = #tag-lit name, rest = rest }
     | [#lparen, #rparen] ++ rest -> { result = #hole, rest = rest }
     | [#lparen] ++ rest ->
          (| { result = inner, rest = rest2 } ->
               (| [#rparen] ++ rest3 -> { result = inner, rest = rest3 }
                | _ -> { result = inner, rest = rest2 }
               ) rest2
          ) (parse-expr rest)
     | [#lbracket, #rbracket] ++ rest -> { result = #list [], rest = rest }
     | [#lbracket] ++ rest -> parse-list rest
     | [#lbrace] ++ rest -> parse-record rest
     | [#pipe] ++ _t -> parse-case tokens
     | _ -> { result = #hole, rest = tokens }
    ) tokens
; try-binop = left -> tokens ->
    (| [#plus] ++ rest ->
         (| { result = right, rest = rest2 } ->
             try-binop (#binop { op = "+", left = left, right = right }) rest2
         ) (parse-app rest)
     | [#minus] ++ rest ->
         (| { result = right, rest = rest2 } ->
             try-binop (#binop { op = "-", left = left, right = right }) rest2
         ) (parse-app rest)
     | [#star] ++ rest ->
         (| { result = right, rest = rest2 } ->
             try-binop (#binop { op = "*", left = left, right = right }) rest2
         ) (parse-app rest)
     | [#slash] ++ rest ->
         (| { result = right, rest = rest2 } ->
             try-binop (#binop { op = "/", left = left, right = right }) rest2
         ) (parse-app rest)
     | [#percent] ++ rest ->
         (| { result = right, rest = rest2 } ->
             try-binop (#binop { op = "%", left = left, right = right }) rest2
         ) (parse-app rest)
     | [#plus-plus] ++ rest ->
         (| { result = right, rest = rest2 } ->
             try-binop (#binop { op = "++", left = left, right = right }) rest2
         ) (parse-app rest)
     | [#gt-plus] ++ rest ->
         (| { result = right, rest = rest2 } ->
             try-binop (#binop { op = ">+", left = left, right = right }) rest2
         ) (parse-app rest)
     | [#eq-eq] ++ rest ->
         (| { result = right, rest = rest2 } ->
             try-binop (#binop { op = "==", left = left, right = right }) rest2
         ) (parse-app rest)
     | [#bang-eq] ++ rest ->
         (| { result = right, rest = rest2 } ->
             try-binop (#binop { op = "!=", left = left, right = right }) rest2
         ) (parse-app rest)
     | [#lt] ++ rest ->
         (| { result = right, rest = rest2 } ->
             try-binop (#binop { op = "<", left = left, right = right }) rest2
         ) (parse-app rest)
     | [#gt] ++ rest ->
         (| { result = right, rest = rest2 } ->
             try-binop (#binop { op = ">", left = left, right = right }) rest2
         ) (parse-app rest)
     | [#lt-eq] ++ rest ->
         (| { result = right, rest = rest2 } ->
             try-binop (#binop { op = "<=", left = left, right = right }) rest2
         ) (parse-app rest)
     | [#gt-eq] ++ rest ->
         (| { result = right, rest = rest2 } ->
             try-binop (#binop { op = ">=", left = left, right = right }) rest2
         ) (parse-app rest)
     | _ -> { result = left, rest = tokens }
    ) tokens
; parse-case = tokens ->
    collect-arms tokens []
; collect-arms = tokens -> arms-rev ->
    (| [#pipe] ++ rest ->
         (| { result = pat, rest = rest2 } ->
              (| [#arrow] ++ rest3 ->
                   (| { result = body, rest = rest4 } ->
                       collect-arms rest4 ({ pat = pat, body = body } >+ arms-rev)
                   ) (parse-lam rest3)
               | _ ->
                   { result = #case (list/reverse arms-rev), rest = tokens }
              ) rest2
         ) (parse-app rest)
     | _ -> { result = #case (list/reverse arms-rev), rest = tokens }
    ) tokens
; parse-list = tokens ->
    collect-list tokens []
; collect-list = tokens -> items-rev ->
    (| [#rbracket] ++ rest ->
         { result = #list (list/reverse items-rev), rest = rest }
     | _ ->
         (| { result = item, rest = rest1 } ->
              (| [#comma] ++ rest2 ->
                   collect-list rest2 (item >+ items-rev)
               | [#rbracket] ++ rest2 ->
                   { result = #list (list/reverse (item >+ items-rev)), rest = rest2 }
               | _ ->
                   { result = #list (list/reverse (item >+ items-rev)), rest = rest1 }
              ) rest1
         ) (parse-lam tokens)
    ) tokens
; parse-record = tokens ->
    collect-fields tokens []
; collect-fields = tokens -> fields-rev ->
    (| [#rbrace] ++ rest ->
         { result = #record (list/reverse fields-rev), rest = rest }
     | [#ident name, #eq] ++ rest ->
         (| { result = val, rest = rest2 } ->
              (| [#comma] ++ rest3 ->
                   collect-fields rest3 ({ name = name, val = val } >+ fields-rev)
               | [#rbrace] ++ rest3 ->
                   { result = #record (list/reverse ({ name = name, val = val } >+ fields-rev)), rest = rest3 }
               | _ ->
                   { result = #record (list/reverse ({ name = name, val = val } >+ fields-rev)), rest = rest2 }
              ) rest2
         ) (parse-lam rest)
     | _ ->
         { result = #record (list/reverse fields-rev), rest = tokens }
    ) tokens
