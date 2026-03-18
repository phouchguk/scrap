{ eval = eval, apply = apply }
; eval = ast -> env ->
    (| #lit-int n -> #int n
     | #lit-float f -> #float f
     | #lit-text t -> #text t
     | #hole -> #nil
     | #nil -> #nil
     | #var name -> env-lookup name env
     | #tag-lit name -> #tag name
     | #lam { param = p, body = b } -> #closure { param = p, body = b, env = env }
     | #case branches -> #case-fn { branches = branches, env = env }
     | #app { fn = fn-ast, arg = arg-ast } ->
         apply (eval fn-ast env) (eval arg-ast env)
     | #binop { op = op, left = l, right = r } ->
         eval-binop op (eval l env) (eval r env)
     | #where { body = body, binds = binds } ->
         eval body (extend-env env binds)
     | #list items ->
         list/fold
           (acc -> item -> #cons { head = eval item env, tail = acc })
           #nil
           (list/reverse items)
     | #record fields ->
         #record (eval-fields fields env)
     | #field-access { record = r, field = name } ->
         field-get name (eval r env)
     | _ -> #error
    ) ast
; env-lookup = name -> env ->
    (| [] -> #error
     | [{ name = n, val = v }] ++ rest ->
         (| #true -> v | #false -> env-lookup name rest) (n == name)
    ) env
; extend-env = env -> binds ->
    list/fold (e -> b -> { name = b.name, val = eval b.val e } >+ e) env binds
; eval-binop = op -> lv -> rv ->
    (| "+" ->
         (| #int a -> (| #int b -> #int (a + b) | _ -> #error) rv
          | #float a -> (| #float b -> #float (a + b) | _ -> #error) rv
          | #text a -> (| #text b -> #text (a ++ b) | _ -> #error) rv
          | _ -> #error
         ) lv
     | "-" ->
         (| #int a -> (| #int b -> #int (a - b) | _ -> #error) rv
          | #float a -> (| #float b -> #float (a - b) | _ -> #error) rv
          | _ -> #error
         ) lv
     | "*" ->
         (| #int a -> (| #int b -> #int (a * b) | _ -> #error) rv
          | #float a -> (| #float b -> #float (a * b) | _ -> #error) rv
          | _ -> #error
         ) lv
     | "/" ->
         (| #int a -> (| #int b -> #int (a / b) | _ -> #error) rv
          | #float a -> (| #float b -> #float (a / b) | _ -> #error) rv
          | _ -> #error
         ) lv
     | "%" ->
         (| #int a -> (| #int b -> #int (a % b) | _ -> #error) rv
          | _ -> #error
         ) lv
     | "++" ->
         (| #text a -> (| #text b -> #text (a ++ b) | _ -> #error) rv
          | _ -> #error
         ) lv
     | ">+" -> #cons { head = lv, tail = rv }
     | "==" -> scrap-bool (lv == rv)
     | "!=" -> scrap-bool (lv != rv)
     | "<"  ->
         (| #int a -> (| #int b -> scrap-bool (a < b) | _ -> #error) rv
          | #float a -> (| #float b -> scrap-bool (a < b) | _ -> #error) rv
          | _ -> #error
         ) lv
     | ">"  ->
         (| #int a -> (| #int b -> scrap-bool (a > b) | _ -> #error) rv
          | #float a -> (| #float b -> scrap-bool (a > b) | _ -> #error) rv
          | _ -> #error
         ) lv
     | "<=" ->
         (| #int a -> (| #int b -> scrap-bool (a <= b) | _ -> #error) rv
          | _ -> #error
         ) lv
     | ">=" ->
         (| #int a -> (| #int b -> scrap-bool (a >= b) | _ -> #error) rv
          | _ -> #error
         ) lv
     | _ -> #error
    ) op
; scrap-bool = b -> (| #true -> #tag "true" | #false -> #tag "false") b
; eval-fields = fields -> env ->
    list/fold
      (acc -> f -> { name = f.name, val = eval f.val env } >+ acc)
      []
      (list/reverse fields)
; field-get = name -> rec-val ->
    (| #record fields -> fields-find name fields
     | _ -> #error
    ) rec-val
; fields-find = name -> fields ->
    (| [] -> #error
     | [{ name = n, val = v }] ++ rest ->
         (| #true -> v | #false -> fields-find name rest) (name == n)
    ) fields
; apply = fn -> arg ->
    (| #closure { param = p, body = b, env = cenv } ->
         (| #just new-env -> eval b new-env
          | #nothing -> #error
          | _ -> #error
         ) (try-match p arg cenv)
     | #case-fn { branches = branches, env = cenv } ->
         apply-case branches arg cenv
     | #tag name -> #tag-val { name = name, val = arg }
     | _ -> #error
    ) fn
; apply-case = branches -> arg -> env ->
    (| [] -> #error
     | [{ pat = pat, body = body }] ++ rest ->
         (| #just new-env -> eval body new-env
          | #nothing -> apply-case rest arg env
          | _ -> apply-case rest arg env
         ) (try-match pat arg env)
    ) branches
; try-match = pat -> arg -> env ->
    (| #var name ->
         #just ({ name = name, val = arg } >+ env)
     | #hole ->
         #just env
     | #tag-lit name ->
         (| #tag t -> (| #true -> #just env | #false -> #nothing) (name == t)
          | _ -> #nothing
         ) arg
     | #lit-int n ->
         (| #int m -> (| #true -> #just env | #false -> #nothing) (n == m)
          | _ -> #nothing
         ) arg
     | #lit-float f ->
         (| #float g -> (| #true -> #just env | #false -> #nothing) (f == g)
          | _ -> #nothing
         ) arg
     | #lit-text t ->
         (| #text s -> (| #true -> #just env | #false -> #nothing) (t == s)
          | _ -> #nothing
         ) arg
     | _ -> #nothing
    ) pat
