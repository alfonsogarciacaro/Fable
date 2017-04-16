import fable from 'rollup-plugin-fable';
var path = require('path');

function resolve(filePath) {
  return path.resolve(__dirname, filePath)
}

// var babelOptions = {
//   "presets": [
//     [resolve("../../../../node_modules/babel-preset-es2015"), {"modules": false}],
//     //[resolve("../../../../node_modules/babel-preset-babili"), {}]
//   ]
// };

var fableOptions = {
  //babel: babelOptions,
  fableCore: resolve("../../../../build/fable-core"),
  //plugins: [],
  define: [
    "COMPILER_SERVICE",
    "FX_NO_CORHOST_SIGNER",
    "FX_NO_LINKEDRESOURCES",
    "FX_NO_PDB_READER",
    "FX_NO_PDB_WRITER",
    "FX_NO_WEAKTABLE",
    "NO_COMPILER_BACKEND",
    "NO_INLINE_IL_PARSER",
    "TRACE"
  ]
};

export default {
  entry: resolve('./testapp.fsproj'),
  dest: resolve('./out/bundle.js'),
  format: 'cjs', // 'amd', 'cjs', 'es', 'iife', 'umd'
  //sourceMap: 'inline',
  plugins: [
    fable(fableOptions),
  ],
};
