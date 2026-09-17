using System.Runtime.CompilerServices;

// The letter package's tests live in Wakeel.Core.Tests, because adding a test project to the
// solution is not B3-1b's to do. Several of the pieces worth testing on their own — how a mark is
// read out of a line, how a shape's anchor is rewritten, what counts as a blank paragraph — are
// internal on purpose: they are the composer's workings, not an API for the screens.
[assembly: InternalsVisibleTo("Wakeel.Core.Tests")]
