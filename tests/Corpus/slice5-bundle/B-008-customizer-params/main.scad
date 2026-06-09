include <lib.scad>
/* [Box] */
width = 10;  // [1:100]
height = 20;
ratio = width / height;

part(ratio);
