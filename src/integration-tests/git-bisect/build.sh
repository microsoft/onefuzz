#!/bin/bash

set -e

git init test
(cp src/Makefile test; cd test; git add Makefile)
for i in $(seq 0 8); do
    cp src/fuzz.c test/fuzz.c
    for j in $(seq $i 8); do
        if [ $i != $j ]; then
            sed -i /TEST$j/d test/fuzz.c
        fi
    done
    (cd test; git add fuzz.c; git commit -m "commit $i")
done