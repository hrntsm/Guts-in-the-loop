// Run scripts/bootstrap.ps1 -IconTools to install the pinned renderer first.
const fs = require('node:fs');
const path = require('node:path');
const { Resvg } = require('../tools/icon-renderer/node_modules/@resvg/resvg-js');

const resources = path.join(__dirname, '../src/SequentialGallery/Resources');
for (const name of ['Gallery', 'Rendering']) {
  const svg = fs.readFileSync(path.join(resources, `${name}.svg`));
  const png = new Resvg(svg).render().asPng();
  fs.writeFileSync(path.join(resources, `${name}.png`), png);
  console.log(`Rendered ${name}.svg to ${name}.png (24x24, transparent).`);
}
