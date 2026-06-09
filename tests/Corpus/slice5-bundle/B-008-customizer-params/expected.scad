/* [Box] */
width = 10;  // [1:100]
height = 20;
ratio = width / height;

/* [Hidden] */
LIBCONST = 5;
module part(h) cube([LIBCONST, LIBCONST, h]);

part(ratio);
