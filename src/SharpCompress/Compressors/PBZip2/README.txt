This BZIP2 implementation is based on code from Jaime Olivares, https://github.com/jaime-olivares/bzip2

It has a modification adding multi threadded block processing, https://github.com/drone1400/bzip2

Jaime Olivares' implementation is itself based on a Java implementation by Mateusz Bartosiewicz, https://github.com/MateuszBartosiewicz/bzip2
Supposedly this version has a compression bug: https://github.com/MateuszBartosiewicz/bzip2/issues/1
So far, I have not encountered this issue, but if anyone wants to use this in the future, be advised, this could be a problem

- drone1400
