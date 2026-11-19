# Sessions and jobs are different things

Loading a game can take minutes and a lot of memory, so repeated one-shot loads are a poor fit when an agent needs to list, inspect, then export the same files. A Session is a local background process that holds a loaded set of game files. `session start` returns its id immediately, and load progress is `loading`, `ready`, or `failed` on that session.

A Job is only a long-running export or similar write. Load progress is not modeled as a Job, so agents do not have to juggle two identifiers for the same load. We rejected reusing the existing web GUI server as a session, and rejected a full on-disk reload cache for the first version.

Idle sessions exit after 30 minutes or `session close`. After a machine restart they are gone.
