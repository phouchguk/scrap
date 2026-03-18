list/map fib [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10]
; fib = | 0 -> 0
        | 1 -> 1
        | n -> fib (n - 1) + fib (n - 2)
