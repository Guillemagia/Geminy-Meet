const express = require("express");
const cors = require("cors");
const http = require("http");
const { Server } = require("socket.io");

const app = express();

app.use(cors());
app.use(express.json());

const server = http.createServer(app);

const io = new Server(server, {
  cors: {
    origin: "*"
  }
});

/*
 Base de datos temporal
*/
const users = [
  {
    id: 1,
    username: "guill",
    credits: 100
  }
];

/*
 Estado servidor
*/
app.get("/", (req, res) => {
  res.send("Geminy Meet Backend funcionando");
});

/*
 Consultar créditos
*/
app.get("/credits/:id", (req, res) => {

  const user = users.find(
    u => u.id == req.params.id
  );

  if (!user) {
    return res.status(404).json({
      error: "Usuario no encontrado"
    });
  }

  res.json(user);

});

/*
 Recargar créditos
*/
app.post("/credits/add", (req, res) => {

  const { id, amount } = req.body;

  const user = users.find(
    u => u.id == id
  );

  if (!user) {
    return res.status(404).json({
      error: "Usuario no encontrado"
    });
  }

  user.credits += amount;

  res.json({
    success: true,
    credits: user.credits
  });

});

/*
 Gastar créditos
*/
app.post("/credits/use", (req, res) => {

  const { id, amount } = req.body;

  const user = users.find(
    u => u.id == id
  );

  if (!user) {
    return res.status(404).json({
      error: "Usuario no encontrado"
    });
  }

  if (user.credits < amount) {
    return res.status(400).json({
      error: "Créditos insuficientes"
    });
  }

  user.credits -= amount;

  res.json({
    success: true,
    credits: user.credits
  });

});

/*
 Chat Socket.IO
*/
io.on("connection", (socket) => {

  console.log("Usuario conectado");

  socket.on("message", (data) => {

    io.emit("message", {
      user: data.user,
      text: data.text,
      createdAt: new Date()
    });

  });

  socket.on("disconnect", () => {
    console.log("Usuario desconectado");
  });

});

const PORT = process.env.PORT || 3000;

server.listen(PORT, () => {
  console.log(`Servidor iniciado en puerto ${PORT}`);
});