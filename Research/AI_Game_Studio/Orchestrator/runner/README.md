# Offline studio decision checker

Python3.10+ standard library. No network, agents, spending or UI actions.
Validate trusted JSON input and report missing evidence, budget/cash gaps and
readiness gates. An assessment is not authorization, authenticated evidence or
a profit guarantee. The public template has no preset personal budget.

Run from this directory:

```sh
python -B -m unittest discover -s tests -v
python runner.py init
python b2b_quote.py examples/b2b_quote.synthetic.json
```

Use `python b2b_quote.py --help` for its actual CLI arguments. All bundled test
scenarios are synthetic. The component catalog describes requirements, not a
fully connected production service. Existing private commercial inputs are not
included in this branch.
