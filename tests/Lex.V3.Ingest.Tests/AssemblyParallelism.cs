using Microsoft.VisualStudio.TestTools.UnitTesting;

// CLASS SCOPE, NOT METHOD SCOPE, AND THE DIFFERENCE IS THE POINT. Tests inside one class still run
// one after another, so a class that shares a fixture, a store or a sequence between its own methods
// keeps the ordering it was written against. Only whole classes run alongside each other.
//
// The classes that cannot tolerate even that already say so: [DoNotParallelize] was applied to
// twenty-five of them before any of this was switched on. Those markers were an untested assumption
// until now; this is what tests them.
//
// Workers = 0 asks the runner for one worker per logical processor. The machine slot the merged
// wrapper holds is what keeps two runs from competing; this only decides how much of the machine a
// single run uses, and before this it used two processors out of sixteen.
[assembly: Parallelize(Workers = 0, Scope = ExecutionScope.ClassLevel)]
