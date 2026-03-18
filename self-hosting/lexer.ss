{ lex = lex }
; lex = src -> lex-chars (text/chars src) []
; lex-chars = chars -> acc ->
    (| [] -> list/reverse acc
     | [c] ++ rest ->
         (| #true -> lex-chars rest acc
          | #false ->
              (| #true -> lex-number c rest acc
               | #false ->
                   (| #true -> lex-tag rest acc
                    | #false ->
                        (| #true -> lex-ident c rest acc
                         | #false -> lex-punct c rest acc
                        ) (is-letter-or-us c)
                   ) (c == "#")
              ) (is-digit c)
         ) (is-ws c)
    ) chars
; is-ws = | " " -> #true | "\n" -> #true | "\r" -> #true | "\t" -> #true | _ -> #false
; is-digit = c -> (| #true -> c <= "9" | #false -> #false) (c >= "0")
; is-letter = c ->
    (| #true -> c <= "z"
     | #false -> (| #true -> c <= "Z" | #false -> #false) (c >= "A")
    ) (c >= "a")
; is-letter-or-digit = c -> (| #true -> #true | #false -> is-digit c) (is-letter c)
; is-letter-or-us = c -> (| #true -> #true | #false -> c == "_") (is-letter c)
; is-ident-cont = c ->
    (| #true -> #true
     | #false ->
         (| #true -> #true
          | #false ->
              (| #true -> #true
               | #false -> (| #true -> #true | #false -> #false) (c == "_")
              ) (c == "/")
         ) (c == "-")
    ) (is-letter-or-digit c)
; join-chars = chars -> string/join "" chars
; scan-digits = chars -> acc ->
    (| [] -> { digits = list/reverse acc, rest = [] }
     | [c] ++ rest ->
         (| #true -> scan-digits rest (c >+ acc)
          | #false -> { digits = list/reverse acc, rest = chars }
         ) (is-digit c)
    ) chars
; scan-ident = chars -> acc ->
    (| [] -> { word = list/reverse acc, rest = [] }
     | [c] ++ rest ->
         (| #true -> scan-ident rest (c >+ acc)
          | #false -> { word = list/reverse acc, rest = chars }
         ) (is-ident-cont c)
    ) chars
; lex-number = c -> rest -> acc ->
    (| { digits = d, rest = rest2 } ->
        check-float (join-chars d) rest2 acc
    ) (scan-digits rest [c])
; check-float = int-text -> rest -> acc ->
    (| ["."] ++ rest2 ->
         (| [d] ++ rest3 ->
              (| #true ->
                   (| { digits = fd, rest = rest4 } ->
                       lex-chars rest4 (#float (text/to-float (int-text ++ "." ++ join-chars fd)) >+ acc)
                   ) (scan-digits rest3 [d])
               | #false -> lex-chars rest (#int (text/to-int int-text) >+ acc)
              ) (is-digit d)
          | _ -> lex-chars rest (#int (text/to-int int-text) >+ acc)
         ) rest2
     | _ -> lex-chars rest (#int (text/to-int int-text) >+ acc)
    ) rest
; lex-ident = c -> rest -> acc ->
    (| { word = w, rest = rest2 } ->
        lex-chars rest2 (#ident (join-chars w) >+ acc)
    ) (scan-ident rest [c])
; lex-tag = rest -> acc ->
    (| { word = w, rest = rest2 } ->
        lex-chars rest2 (#tag (join-chars w) >+ acc)
    ) (scan-ident rest [])
; lex-string = chars -> str-acc -> outer-acc ->
    (| [] ->
         lex-chars [] (#text (join-chars (list/reverse str-acc)) >+ outer-acc)
     | ["\""] ++ rest ->
         lex-chars rest (#text (join-chars (list/reverse str-acc)) >+ outer-acc)
     | ["\\", "\""] ++ rest ->
         lex-string rest ("\"" >+ str-acc) outer-acc
     | ["\\", "n"] ++ rest ->
         lex-string rest ("\n" >+ str-acc) outer-acc
     | ["\\", "t"] ++ rest ->
         lex-string rest ("\t" >+ str-acc) outer-acc
     | ["\\", "\\"] ++ rest ->
         lex-string rest ("\\" >+ str-acc) outer-acc
     | [c] ++ rest ->
         lex-string rest (c >+ str-acc) outer-acc
    ) chars
; lex-comment = chars -> acc ->
    (| [] -> list/reverse acc
     | ["\n"] ++ rest -> lex-chars rest acc
     | [c] ++ rest -> lex-comment rest acc
    ) chars
; lex-minus = rest -> acc ->
    (| ["-"] ++ rest2 -> lex-comment rest2 acc
     | [">"] ++ rest2 -> lex-chars rest2 (#arrow >+ acc)
     | _ -> lex-chars rest (#minus >+ acc)
    ) rest
; lex-plus = rest -> acc ->
    (| ["+"] ++ rest2 -> lex-chars rest2 (#plus-plus >+ acc)
     | _ -> lex-chars rest (#plus >+ acc)
    ) rest
; lex-gt = rest -> acc ->
    (| ["+"] ++ rest2 -> lex-chars rest2 (#gt-plus >+ acc)
     | [">"] ++ rest2 -> lex-chars rest2 (#gt-gt >+ acc)
     | ["="] ++ rest2 -> lex-chars rest2 (#gt-eq >+ acc)
     | _ -> lex-chars rest (#gt >+ acc)
    ) rest
; lex-lt = rest -> acc ->
    (| ["="] ++ rest2 -> lex-chars rest2 (#lt-eq >+ acc)
     | _ -> lex-chars rest (#lt >+ acc)
    ) rest
; lex-eq = rest -> acc ->
    (| ["="] ++ rest2 -> lex-chars rest2 (#eq-eq >+ acc)
     | _ -> lex-chars rest (#eq >+ acc)
    ) rest
; lex-bang = rest -> acc ->
    (| ["="] ++ rest2 -> lex-chars rest2 (#bang-eq >+ acc)
     | _ -> lex-chars rest acc
    ) rest
; lex-punct = c -> rest -> acc ->
    (| "+" -> lex-plus rest acc
     | "-" -> lex-minus rest acc
     | ">" -> lex-gt rest acc
     | "<" -> lex-lt rest acc
     | "=" -> lex-eq rest acc
     | "!" -> lex-bang rest acc
     | "\"" -> lex-string rest [] acc
     | "|" -> lex-chars rest (#pipe >+ acc)
     | ";" -> lex-chars rest (#semi >+ acc)
     | ":" -> lex-chars rest (#colon >+ acc)
     | "(" -> lex-chars rest (#lparen >+ acc)
     | ")" -> lex-chars rest (#rparen >+ acc)
     | "[" -> lex-chars rest (#lbracket >+ acc)
     | "]" -> lex-chars rest (#rbracket >+ acc)
     | "{" -> lex-chars rest (#lbrace >+ acc)
     | "}" -> lex-chars rest (#rbrace >+ acc)
     | "," -> lex-chars rest (#comma >+ acc)
     | "." -> lex-chars rest (#dot >+ acc)
     | "*" -> lex-chars rest (#star >+ acc)
     | "/" -> lex-chars rest (#slash >+ acc)
     | "%" -> lex-chars rest (#percent >+ acc)
     | _ -> lex-chars rest acc
    ) c
