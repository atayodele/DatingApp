﻿using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using AutoMapper;
using DatingApp.API.Data;
using DatingApp.API.Dto;
using DatingApp.API.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace DatingApp.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly IAuthRepository _repo;
        private readonly IConfiguration _configuration;
        private readonly IMapper _mapper;

        public AuthController(
            IAuthRepository repo,
            IConfiguration configuration,
            IMapper mapper)
        {
            _repo = repo;
            _configuration = configuration;
            _mapper = mapper;
        }
        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] UserRegisterDto userRegisterDto)
        {
            if(!string.IsNullOrEmpty(userRegisterDto.Username))
                userRegisterDto.Username = userRegisterDto.Username.ToLower();
            if (await _repo.UserExists(userRegisterDto.Username))
                ModelState.AddModelError("Username","Username is already taken");
            // validate request
            if (!ModelState.IsValid)
                return BadRequest(ModelState);
            var userToCreate = _mapper.Map<User>(userRegisterDto);
            var createUser = await _repo.Register(userToCreate, userRegisterDto.Password);
            var userToReturn = _mapper.Map<UserForDetailedDto>(createUser);
            return CreatedAtRoute("GetUser", new { controller = "Users", id = createUser.Id }, userToReturn);
        }
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody]UserForLoginDto userForLoginDto)
        {
            var userFromRepo = await _repo.Login(userForLoginDto.Username, userForLoginDto.Password);
            if (userFromRepo == null)
                return Unauthorized();
            //generate token
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.ASCII.GetBytes(_configuration.GetSection("AppSettings:Token").Value);
            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new Claim[]
                {
                    new Claim(ClaimTypes.NameIdentifier, userFromRepo.Id.ToString()),
                    new Claim(ClaimTypes.Name, userFromRepo.Username)
                }),
                Expires = DateTime.Now.AddDays(1),
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha512Signature)
            };
            var token = tokenHandler.CreateToken(tokenDescriptor);
            var tokenString = tokenHandler.WriteToken(token);

            var user = _mapper.Map<UserForListDto>(userFromRepo);

            return Ok(new { tokenString, user });
        }
    }
}