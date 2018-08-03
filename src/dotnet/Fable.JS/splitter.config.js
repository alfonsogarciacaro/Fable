const useCommonjs = process.argv.find(v => v === "--commonjs");
console.log("Compiling to " + (useCommonjs ? "commonjs" : "ES2015 modules") + "...")

const babelOptions = useCommonjs
  ? { plugins: ["transform-es2015-modules-commonjs"] }
  : {
    // presets: [
    //   // Uglify-js will fail if we don't compile to ES5
    //   ["es2015", { modules: false }]
    // ]
  };

const fableOptions = {
  define: [
    "FX_NO_CORHOST_SIGNER",
    "FX_NO_LINKEDRESOURCES",
    "FX_NO_PDB_READER",
    "FX_NO_PDB_WRITER",
    "FX_NO_WEAKTABLE",
    "FX_REDUCED_EXCEPTIONS",
    "NO_COMPILER_BACKEND",
    "NO_EXTENSIONTYPING",
    "NO_INLINE_IL_PARSER"
  ],
};

module.exports = {
  entry: "./Fable.JS.fsproj",
  outDir: "./out",
  // port: 61225,
  babel: babelOptions,
  fable: fableOptions,
};
